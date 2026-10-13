using System.Collections.Concurrent;
using System.Threading.Channels;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentEmpireSystem.Infrastructure.Services;

public sealed class WorkloadOptions
{
    public int MaxConcurrentFfmpeg { get; set; } = 1;
    public int MaxConcurrentAiGeneration { get; set; } = 1;
    public int MaxConcurrentProduction { get; set; } = 1;
    public int ProductionQueueCapacity { get; set; } = 32;
    public int ProjectLockWaitSeconds { get; set; } = 3;
    /// <summary>Max seconds to wait for queue space before returning QueueFull.</summary>
    public int QueueWaitSeconds { get; set; } = 90;
}

public enum WorkloadSlot
{
    Ffmpeg,
    AiGeneration,
    Production
}

public interface IWorkloadGate
{
    Task<IAsyncDisposable> AcquireAsync(WorkloadSlot slot, Guid? projectId = null, CancellationToken ct = default);
}

public sealed class WorkloadGate(IOptions<WorkloadOptions> options) : IWorkloadGate
{
    private static readonly AsyncLocal<Dictionary<Guid, int>?> ProjectLockDepth = new();

    private readonly WorkloadOptions _options = options.Value;
    private readonly SemaphoreSlim _ffmpeg = new(Math.Max(1, options.Value.MaxConcurrentFfmpeg));
    private readonly SemaphoreSlim _ai = new(Math.Max(1, options.Value.MaxConcurrentAiGeneration));
    private readonly SemaphoreSlim _production = new(Math.Max(1, options.Value.MaxConcurrentProduction));
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _projectLocks = new();

    public async Task<IAsyncDisposable> AcquireAsync(WorkloadSlot slot, Guid? projectId = null, CancellationToken ct = default)
    {
        var global = slot switch
        {
            WorkloadSlot.Ffmpeg => _ffmpeg,
            WorkloadSlot.AiGeneration => _ai,
            WorkloadSlot.Production => _production,
            _ => throw new ArgumentOutOfRangeException(nameof(slot))
        };

        await global.WaitAsync(ct);

        SemaphoreSlim? projectLock = null;
        Guid? acquiredProjectId = null;
        var ownsProjectSemaphore = false;
        // AI generation often runs alongside rapid UI/API calls; keep global slot throttling,
        // but avoid per-project mutex here to prevent stale-lock false conflicts.
        if (projectId is Guid pid && slot is not WorkloadSlot.AiGeneration)
        {
            var depths = ProjectLockDepth.Value ??= new Dictionary<Guid, int>();
            projectLock = _projectLocks.GetOrAdd(pid, _ => new SemaphoreSlim(1, 1));
            if (depths.TryGetValue(pid, out var depth) && depth > 0)
            {
                depths[pid] = depth + 1;
                acquiredProjectId = pid;
            }
            else
            {
                var waitMs = Math.Max(1, _options.ProjectLockWaitSeconds) * 1000;
                if (!await projectLock.WaitAsync(waitMs, ct))
                {
                    global.Release();
                    throw new InvalidOperationException(
                        "المشروع مشغول بعملية أخرى. انتظر قليلاً ثم أعد المحاولة.");
                }

                depths[pid] = 1;
                acquiredProjectId = pid;
                ownsProjectSemaphore = true;
            }
        }

        return new GateLease(global, projectLock, acquiredProjectId, ownsProjectSemaphore);
    }

    private sealed class GateLease(
        SemaphoreSlim global,
        SemaphoreSlim? project,
        Guid? projectId,
        bool ownsProjectSemaphore) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return ValueTask.CompletedTask;

            if (projectId is Guid pid)
                ReleaseProjectLock(pid, project, ownsProjectSemaphore);

            global.Release();
            return ValueTask.CompletedTask;
        }
    }

    private static void ReleaseProjectLock(Guid pid, SemaphoreSlim? project, bool ownsProjectSemaphore)
    {
        var depths = ProjectLockDepth.Value;
        if (depths is not null && depths.TryGetValue(pid, out var depth))
        {
            depth--;
            if (depth <= 0)
                depths.Remove(pid);
            else
                depths[pid] = depth;
        }

        // Always release the underlying semaphore if this lease acquired it,
        // even if AsyncLocal depth context was lost on the current continuation.
        if (ownsProjectSemaphore)
        {
            project?.Release();
        }
    }
}

public sealed class ProductionQueueHostedService(
    IServiceScopeFactory scopeFactory,
    IProjectOperationCoordinator operationCoordinator,
    IOptions<WorkloadOptions> options,
    ILogger<ProductionQueueHostedService> logger) : BackgroundService, IProductionQueue
{
    private readonly WorkloadOptions _options = options.Value;
    private readonly ConcurrentDictionary<Guid, byte> _tracked = new();
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(Math.Max(8, options.Value.ProductionQueueCapacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    public async Task<ProductionEnqueueResult> EnqueueAsync(Guid projectId, CancellationToken ct = default)
    {
        if (operationCoordinator.IsBusy(projectId, out _))
            return ProductionEnqueueResult.AlreadyProcessing;

        if (!_tracked.TryAdd(projectId, 0))
            return ProductionEnqueueResult.AlreadyProcessing;

        if (!await TryWriteWithWaitAsync(_channel.Writer, projectId, projectId, ct))
        {
            _tracked.TryRemove(projectId, out _);
            return ProductionEnqueueResult.QueueFull;
        }

        return ProductionEnqueueResult.Queued;
    }

    public bool IsActive(Guid projectId) =>
        _tracked.ContainsKey(projectId) || operationCoordinator.IsBusy(projectId, out _);

    private async Task<bool> TryWriteWithWaitAsync<T>(
        ChannelWriter<T> writer, T item, Guid projectId, CancellationToken ct)
    {
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        waitCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.QueueWaitSeconds)));

        try
        {
            await writer.WaitToWriteAsync(waitCts.Token);
            if (!writer.TryWrite(item))
            {
                _tracked.TryRemove(projectId, out _);
                return false;
            }

            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _tracked.TryRemove(projectId, out _);
            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var projectId in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            if (!operationCoordinator.TryBegin(projectId, ProjectOperationKind.Production, out var blockedBy))
            {
                logger.LogWarning(
                    "Production job dropped for {ProjectId}: {Reason}", projectId, blockedBy);
                _tracked.TryRemove(projectId, out _);
                continue;
            }

            try
            {
                await BackgroundJobScopeRunner.RunAsync(
                    scopeFactory,
                    projectId,
                    async (sp, ct) =>
                    {
                        var pipeline = sp.GetRequiredService<IVideoProductionPipeline>();
                        await pipeline.StartProductionAsync(projectId, ct);
                    },
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Production failed for project {ProjectId}", projectId);
            }
            finally
            {
                operationCoordinator.End(projectId, ProjectOperationKind.Production);
                _tracked.TryRemove(projectId, out _);
            }
        }
    }
}

public sealed class ImageGenerationQueueHostedService(
    IServiceScopeFactory scopeFactory,
    IProjectOperationCoordinator operationCoordinator,
    SystemActivityService activity,
    IOptions<WorkloadOptions> options,
    ILogger<ImageGenerationQueueHostedService> logger) : BackgroundService, IImageGenerationQueue
{
    private readonly WorkloadOptions _options = options.Value;
    private readonly ConcurrentDictionary<Guid, byte> _tracked = new();
    private readonly Channel<ImageGenerationJob> _channel = Channel.CreateBounded<ImageGenerationJob>(
        new BoundedChannelOptions(Math.Max(8, options.Value.ProductionQueueCapacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    public async Task<ImageGenerationEnqueueResult> EnqueueAsync(ImageGenerationJob job, CancellationToken ct = default)
    {
        if (operationCoordinator.IsBusy(job.ProjectId, out _))
            return ImageGenerationEnqueueResult.AlreadyProcessing;

        if (!_tracked.TryAdd(job.ProjectId, 0))
            return ImageGenerationEnqueueResult.AlreadyProcessing;

        if (!await TryWriteWithWaitAsync(job, ct))
        {
            _tracked.TryRemove(job.ProjectId, out _);
            return ImageGenerationEnqueueResult.QueueFull;
        }

        return ImageGenerationEnqueueResult.Queued;
    }

    public bool IsActive(Guid projectId) =>
        _tracked.ContainsKey(projectId) || operationCoordinator.IsBusy(projectId, out _);

    private async Task<bool> TryWriteWithWaitAsync(ImageGenerationJob job, CancellationToken ct)
    {
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        waitCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.QueueWaitSeconds)));

        try
        {
            await _channel.Writer.WaitToWriteAsync(waitCts.Token);
            if (!_channel.Writer.TryWrite(job))
            {
                _tracked.TryRemove(job.ProjectId, out _);
                return false;
            }

            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _tracked.TryRemove(job.ProjectId, out _);
            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            if (!operationCoordinator.TryBegin(job.ProjectId, ProjectOperationKind.ImageGeneration, out var blockedBy))
            {
                logger.LogWarning(
                    "Image generation job dropped for {ProjectId}: {Reason}", job.ProjectId, blockedBy);
                activity.Fail(job.ProjectId, blockedBy ?? "المشروع مشغول — أعد المحاولة");
                _tracked.TryRemove(job.ProjectId, out _);
                continue;
            }

            try
            {
                await BackgroundJobScopeRunner.RunAsync(
                    scopeFactory,
                    job.ProjectId,
                    async (sp, ct) =>
                    {
                        var images = sp.GetRequiredService<IImageGeneratorService>();
                        await images.GenerateImagesAsync(
                            job.ProjectId, job.Count, job.ReplaceExisting, job.Provider, ct);
                    },
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Image generation failed for project {ProjectId}", job.ProjectId);
                activity.Fail(job.ProjectId, ex.Message);
            }
            finally
            {
                operationCoordinator.End(job.ProjectId, ProjectOperationKind.ImageGeneration);
                _tracked.TryRemove(job.ProjectId, out _);
            }
        }
    }
}

public sealed class VideoGenerationQueueHostedService(
    IServiceScopeFactory scopeFactory,
    IProjectOperationCoordinator operationCoordinator,
    SystemActivityService activity,
    IOptions<WorkloadOptions> options,
    ILogger<VideoGenerationQueueHostedService> logger) : BackgroundService, IVideoGenerationQueue
{
    private readonly WorkloadOptions _options = options.Value;
    private readonly ConcurrentDictionary<Guid, byte> _tracked = new();
    private readonly Channel<VideoGenerationJob> _channel = Channel.CreateBounded<VideoGenerationJob>(
        new BoundedChannelOptions(Math.Max(8, options.Value.ProductionQueueCapacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    public async Task<VideoGenerationEnqueueResult> EnqueueAsync(VideoGenerationJob job, CancellationToken ct = default)
    {
        if (operationCoordinator.IsBusy(job.ProjectId, out _))
            return VideoGenerationEnqueueResult.AlreadyProcessing;

        if (!_tracked.TryAdd(job.ProjectId, 0))
            return VideoGenerationEnqueueResult.AlreadyProcessing;

        if (!await TryWriteWithWaitAsync(job, ct))
        {
            _tracked.TryRemove(job.ProjectId, out _);
            return VideoGenerationEnqueueResult.QueueFull;
        }

        return VideoGenerationEnqueueResult.Queued;
    }

    public bool IsActive(Guid projectId) =>
        _tracked.ContainsKey(projectId) || operationCoordinator.IsBusy(projectId, out _);

    private async Task<bool> TryWriteWithWaitAsync(VideoGenerationJob job, CancellationToken ct)
    {
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        waitCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.QueueWaitSeconds)));

        try
        {
            await _channel.Writer.WaitToWriteAsync(waitCts.Token);
            if (!_channel.Writer.TryWrite(job))
            {
                _tracked.TryRemove(job.ProjectId, out _);
                return false;
            }

            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _tracked.TryRemove(job.ProjectId, out _);
            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            var opKind = job.Mode == VideoGenerationMode.AnimateImages
                ? ProjectOperationKind.VideoAnimation
                : ProjectOperationKind.VideoGeneration;

            if (!operationCoordinator.TryBegin(job.ProjectId, opKind, out var blockedBy))
            {
                logger.LogWarning(
                    "Video generation job dropped for {ProjectId}: {Reason}", job.ProjectId, blockedBy);
                activity.Fail(job.ProjectId, blockedBy ?? "المشروع مشغول — أعد المحاولة");
                _tracked.TryRemove(job.ProjectId, out _);
                continue;
            }

            try
            {
                await BackgroundJobScopeRunner.RunAsync(
                    scopeFactory,
                    job.ProjectId,
                    async (sp, ct) =>
                    {
                        var videos = sp.GetRequiredService<IVideoGeneratorService>();
                        if (job.Mode == VideoGenerationMode.AnimateImages)
                        {
                            await videos.AnimateImagesToVideoAsync(
                                job.ProjectId, job.ReplaceExisting, job.Provider, ct);
                        }
                        else
                        {
                            await videos.GenerateVideosFromPromptsAsync(
                                job.ProjectId, job.Count, job.ReplaceExisting, job.Provider, ct);
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
                logger.LogError(ex, "Video generation failed for project {ProjectId}", job.ProjectId);
                activity.Fail(job.ProjectId, ex.Message);
            }
            finally
            {
                operationCoordinator.End(job.ProjectId, opKind);
                _tracked.TryRemove(job.ProjectId, out _);
            }
        }
    }
}
