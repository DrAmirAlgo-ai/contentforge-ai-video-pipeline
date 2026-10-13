using System.Collections.Concurrent;
using System.Threading.Channels;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentEmpireSystem.Infrastructure.Services;

public sealed class AiLlmGenerationQueueHostedService(
    IServiceScopeFactory scopeFactory,
    IProjectOperationCoordinator operationCoordinator,
    SystemActivityService activity,
    IOptions<WorkloadOptions> options,
    ILogger<AiLlmGenerationQueueHostedService> logger) : BackgroundService, IAiLlmGenerationQueue
{
    private readonly WorkloadOptions _options = options.Value;
    private readonly ConcurrentDictionary<Guid, byte> _tracked = new();
    private readonly Channel<AiLlmGenerationJob> _channel = Channel.CreateBounded<AiLlmGenerationJob>(
        new BoundedChannelOptions(Math.Max(8, options.Value.ProductionQueueCapacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    public async Task<AiLlmEnqueueResult> EnqueueAsync(AiLlmGenerationJob job, CancellationToken ct = default)
    {
        var trackId = job.ProjectId ?? Guid.NewGuid();
        if (job.ProjectId.HasValue && operationCoordinator.IsBusy(job.ProjectId.Value, out _))
            return AiLlmEnqueueResult.AlreadyProcessing;

        if (!_tracked.TryAdd(trackId, 0))
            return AiLlmEnqueueResult.AlreadyProcessing;

        if (!await TryWriteAsync(job, trackId, ct))
        {
            _tracked.TryRemove(trackId, out _);
            return AiLlmEnqueueResult.QueueFull;
        }

        return AiLlmEnqueueResult.Queued;
    }

    public bool IsActive(Guid projectId) =>
        _tracked.ContainsKey(projectId) || operationCoordinator.IsBusy(projectId, out _);

    private async Task<bool> TryWriteAsync(AiLlmGenerationJob job, Guid trackId, CancellationToken ct)
    {
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        waitCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.QueueWaitSeconds)));
        try
        {
            await _channel.Writer.WaitToWriteAsync(waitCts.Token);
            if (!_channel.Writer.TryWrite(job))
            {
                _tracked.TryRemove(trackId, out _);
                return false;
            }
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _tracked.TryRemove(trackId, out _);
            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            var trackId = job.ProjectId ?? Guid.NewGuid();
            var opKind = job.Kind switch
            {
                AiLlmJobKind.IdeaGeneration => ProjectOperationKind.IdeaGeneration,
                AiLlmJobKind.ScriptGeneration => ProjectOperationKind.ScriptGeneration,
                _ => ProjectOperationKind.IdeaGeneration
            };

            if (!operationCoordinator.TryBegin(trackId, opKind, out _))
            {
                _tracked.TryRemove(trackId, out _);
                continue;
            }

            try
            {
                await BackgroundJobScopeRunner.RunAsync(
                    scopeFactory,
                    job.ProjectId,
                    async (sp, ct) =>
                    {
                        if (job.Kind == AiLlmJobKind.IdeaGeneration)
                        {
                            var ideas = sp.GetRequiredService<IIdeaGeneratorService>();
                            await ideas.GenerateIdeaAsync(
                                job.CategoryId, job.ProjectId, job.Language, job.UseScriptPromptOnly, ct);
                        }
                        else
                        {
                            if (job.ProjectId is null)
                                throw new InvalidOperationException("معرّف المشروع مطلوب لتوليد السكريبت.");
                            var scripts = sp.GetRequiredService<IScriptGeneratorService>();
                            await scripts.GenerateScriptAsync(
                                job.ProjectId.Value,
                                job.Language,
                                job.UseScriptPromptOnly,
                                job.TargetWordCount,
                                job.ImageSegmentCount,
                                job.ProductionWorkflowPath,
                                ct);
                        }
                    },
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AI LLM job failed ({Kind})", job.Kind);
                activity.Fail(trackId, ex.Message);
            }
            finally
            {
                operationCoordinator.End(trackId, opKind);
                _tracked.TryRemove(trackId, out _);
            }
        }
    }
}

public sealed class PipelineOrchestrationQueueHostedService(
    IServiceScopeFactory scopeFactory,
    IImageGenerationQueue imageQueue,
    IVideoGenerationQueue videoQueue,
    IProductionQueue productionQueue,
    SystemActivityService activity,
    IOptions<WorkloadOptions> options,
    ILogger<PipelineOrchestrationQueueHostedService> logger) : BackgroundService, IPipelineOrchestrationQueue
{
    private readonly WorkloadOptions _options = options.Value;
    private readonly ConcurrentDictionary<Guid, byte> _tracked = new();
    private readonly Channel<PipelineOrchestrationJob> _channel = Channel.CreateBounded<PipelineOrchestrationJob>(
        new BoundedChannelOptions(Math.Max(4, options.Value.ProductionQueueCapacity / 2))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    public async Task<PipelineOrchestrationEnqueueResult> EnqueueAsync(PipelineOrchestrationJob job, CancellationToken ct = default)
    {
        if (!_tracked.TryAdd(job.ProjectId, 0))
            return PipelineOrchestrationEnqueueResult.AlreadyProcessing;

        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        waitCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.QueueWaitSeconds)));
        try
        {
            await _channel.Writer.WaitToWriteAsync(waitCts.Token);
            if (!_channel.Writer.TryWrite(job))
            {
                _tracked.TryRemove(job.ProjectId, out _);
                return PipelineOrchestrationEnqueueResult.QueueFull;
            }
            return PipelineOrchestrationEnqueueResult.Queued;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _tracked.TryRemove(job.ProjectId, out _);
            return PipelineOrchestrationEnqueueResult.QueueFull;
        }
    }

    public bool IsActive(Guid projectId) => _tracked.ContainsKey(projectId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            activity.Start(job.ProjectId, "دورة إنتاج تلقائية", "Orchestrator");
            try
            {
                await RunPipelineAsync(job, stoppingToken);
                activity.Complete(job.ProjectId, "اكتملت دورة الإنتاج التلقائية");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Pipeline orchestration failed for {ProjectId}", job.ProjectId);
                activity.Fail(job.ProjectId, ex.Message);
            }
            finally
            {
                _tracked.TryRemove(job.ProjectId, out _);
            }
        }
    }

    private async Task RunPipelineAsync(PipelineOrchestrationJob job, CancellationToken ct) =>
        await BackgroundJobScopeRunner.RunAsync(
            scopeFactory,
            job.ProjectId,
            (sp, innerCt) => RunPipelineInScopeAsync(sp, job, innerCt),
            ct);

    private async Task RunPipelineInScopeAsync(
        IServiceProvider sp, PipelineOrchestrationJob job, CancellationToken ct)
    {
        var projectService = sp.GetRequiredService<IVideoProjectService>();
        var ideaService = sp.GetRequiredService<IIdeaGeneratorService>();
        var scriptService = sp.GetRequiredService<IScriptGeneratorService>();

        var project = await projectService.GetByIdAsync(job.ProjectId, ct)
            ?? throw new InvalidOperationException("المشروع غير موجود.");

        if (project.Status < VideoProjectStatus.IdeaConfirmed
            && !string.IsNullOrWhiteSpace(project.CurrentIdea))
        {
            await ideaService.ConfirmIdeaAsync(job.ProjectId, ct);
            project = await projectService.GetByIdAsync(job.ProjectId, ct)
                ?? throw new InvalidOperationException("المشروع غير موجود.");
        }

        if (project.Script is null)
        {
            await scriptService.GenerateScriptAsync(
                job.ProjectId,
                project.ContentLanguage,
                useScriptPromptOnly: project.CategoryId is null,
                project.TargetWordCount,
                project.ImageSegmentCount,
                project.ProductionWorkflowPath,
                ct);
            project = await projectService.GetByIdAsync(job.ProjectId, ct)
                ?? throw new InvalidOperationException("المشروع غير موجود.");
        }

        if (job.IncludeImages
            && project.ProductionWorkflowPath != nameof(ProductionWorkflowPath.TextToVideo)
            && project.Workflow?.CurrentStep == ProjectWorkflowStep.FluxImages)
        {
            var imgResult = await imageQueue.EnqueueAsync(
                new ImageGenerationJob(job.ProjectId, 0, false, job.ImageProvider), ct);
            if (imgResult == ImageGenerationEnqueueResult.Queued)
                await WaitForQueueIdleAsync(id => imageQueue.IsActive(id), job.ProjectId, ct);
        }

        project = await projectService.GetByIdAsync(job.ProjectId, ct)
            ?? throw new InvalidOperationException("المشروع غير موجود.");

        var settings = sp.GetRequiredService<ISystemSettingsService>();
        var requireWan = string.Equals(
            await settings.GetOrDefaultAsync(SettingKeys.RequireWanAnimation, "true", ct),
            "true",
            StringComparison.OrdinalIgnoreCase);
        var isTextToVideo = string.Equals(
            project.ProductionWorkflowPath,
            nameof(ProductionWorkflowPath.TextToVideo),
            StringComparison.OrdinalIgnoreCase);

        if (!isTextToVideo
            && requireWan
            && project.Workflow?.CurrentStep == ProjectWorkflowStep.WanVideo)
        {
            var wanJob = new VideoGenerationJob(
                job.ProjectId,
                VideoGenerationMode.AnimateImages,
                0,
                ReplaceExisting: true,
                Provider: null);
            var wanResult = await videoQueue.EnqueueAsync(wanJob, ct);
            if (wanResult == VideoGenerationEnqueueResult.Queued)
                await WaitForQueueIdleAsync(id => videoQueue.IsActive(id), job.ProjectId, ct);
        }

        project = await projectService.GetByIdAsync(job.ProjectId, ct)
            ?? throw new InvalidOperationException("المشروع غير موجود.");

        if (job.IncludeAudio && project.Workflow?.CurrentStep == ProjectWorkflowStep.Audio)
        {
            var tts = sp.GetRequiredService<ITextToSpeechService>();
            await tts.GenerateProjectAudioAsync(job.ProjectId, null, ct);
        }

        if (job.IncludeProduction && project.Workflow?.CanProduce == true)
        {
            var prodResult = await productionQueue.EnqueueAsync(job.ProjectId, ct);
            if (prodResult == ProductionEnqueueResult.Queued)
                await WaitForQueueIdleAsync(id => productionQueue.IsActive(id), job.ProjectId, ct);
        }
    }

    private static async Task WaitForQueueIdleAsync(
        Func<Guid, bool> isActive, Guid projectId, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddHours(2);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (!isActive(projectId))
                return;
            await Task.Delay(1500, ct);
        }
        throw new TimeoutException("انتهت مهلة انتظار خطوة في دورة الإنتاج التلقائية.");
    }
}
