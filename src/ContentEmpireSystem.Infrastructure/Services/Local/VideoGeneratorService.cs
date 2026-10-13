using System.Text.Json;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Entities;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContentEmpireSystem.Infrastructure.Services.Local;

public sealed class VideoGeneratorService(
    ApplicationDbContext db,
    ISystemSettingsService settings,
    IOutputPathService paths,
    ComfyUiClient comfyUi,
    IComfyUiProcessManager comfyProcessManager,
    ILocalGpuOrchestrator gpuOrchestrator,
    IWorkloadGate workloadGate,
    SystemActivityService activity,
    IOperationFileLogger operationLog) : IVideoGeneratorService
{
    public async Task<VideoProjectDto> AnimateImagesToVideoAsync(
        Guid projectId, bool replaceClips = false, string? provider = null, CancellationToken ct = default)
    {
        await EnsureVideoGenerationAllowedAsync(ct);
        const string op = "تحريك الصور (Wan TI2V)";
        operationLog.LogStart(projectId, op, "queue→ComfyUI wan-ti2v");
        activity.Start(projectId, "تحريك الصور", "Wan TI2V");
        try
        {
            await using var workload = await workloadGate.AcquireAsync(WorkloadSlot.AiGeneration, projectId, ct);

            var project = await LoadProjectAsync(projectId, ct);
            if (string.IsNullOrWhiteSpace(project.ScriptJson))
                throw new InvalidOperationException("ولّد السكريبت أولاً.");

            var script = ScriptJsonReader.TryParse(project.ScriptJson)
                ?? throw new InvalidOperationException("بيانات السكريبت غير صالحة. أعد توليد السكريبت.");
            var wanStyle = await settings.GetOrDefaultAsync(SettingKeys.WanVideoPrompt, "", ct);
            var positiveRules = await settings.GetOrDefaultAsync(
                SettingKeys.WanMotionPositivePrompt, WanMotionPromptDefaults.PositiveRules, ct);
            var images = project.GeneratedImages
                .Where(i => !MediaSegmentHelper.IsVideoClip(i.LocalFilePath))
                .OrderBy(i => i.SortOrder)
                .ToList();

            if (images.Count == 0)
                throw new InvalidOperationException("ولّد صور FLUX أولاً.");

            var resolvedProvider = await ResolveVideoProviderAsync(provider, VideoProviderNames.WanTi2v, ct);
            if (!VideoProviderNames.IsLocalComfy(resolvedProvider))
                throw new InvalidOperationException("تحريك الصور يتطلب Wan محلياً عبر ComfyUI.");

            await comfyProcessManager.EnsureReadyForWanAsync(ct);
            await comfyUi.EnsureAvailableAsync(ct);

            await using var gpu = await gpuOrchestrator.AcquireGpuAsync("Wan TI2V", ct);

            var configuredOutput = await settings.GetOrDefaultAsync(SettingKeys.OutputDirectory, "output", ct);
            var clipsDir = Path.Combine(paths.GetProjectDirectory(configuredOutput, projectId), "clips");
            Directory.CreateDirectory(clipsDir);

            foreach (var image in images)
                ResolveUserMotion(script, image.SortOrder, isTextToVideo: false);

            var imageCount = images.Count;
            for (var i = 0; i < imageCount; i++)
            {
                var image = images[i];
                var sourcePath = paths.ResolveSceneMediaPath(
                    configuredOutput, projectId, image.SortOrder, image.LocalFilePath!);
                var motion = ResolveUserMotion(script, image.SortOrder, isTextToVideo: false);
                var videoPrompt = ImagePromptComposer.ComposeForWan(wanStyle, motion, positiveRules);

                var clipPath = await comfyUi.GenerateWanImageToVideoAsync(
                    sourcePath, videoPrompt, clipsDir, image.SortOrder, project, ct, gpuAlreadyPrepared: true);

                image.LocalFilePath = $"{projectId:D}/clips/{Path.GetFileName(clipPath)}";
                image.ImageUrl = null;
                image.AnimationMode = "static";
                project.UpdatedAt = DateTime.UtcNow;
                operationLog.LogFileExchange(projectId, op, sourcePath, clipPath, $"scene {image.SortOrder}");
                await db.SaveChangesAsync(ct);

                if (i < imageCount - 1)
                    await comfyUi.WaitComfyBatchContinuationAsync(ct);
            }

            project.VideoProviderUsed = resolvedProvider;
            project.ProductionWorkflowPath = nameof(ProductionWorkflowPath.ImageToVideo);
            project.ImageProviderUsed ??= ImageProviderNames.ComfyFlux;
            project.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var dto = await ReloadDtoAsync(projectId, ct);
            operationLog.LogComplete(projectId, op, $"{images.Count} clip(s)");
            activity.Complete(projectId, $"{images.Count} مقطع");
            return dto;
        }
        catch (Exception ex)
        {
            operationLog.LogFail(projectId, op, ex.Message, ex);
            activity.Fail(projectId, ex.Message);
            throw;
        }
    }

    public async Task<VideoProjectDto> GenerateVideosFromPromptsAsync(
        Guid projectId, int count, bool replaceExisting, string? provider = null, CancellationToken ct = default)
    {
        await EnsureVideoGenerationAllowedAsync(ct);
        activity.Start(projectId, "توليد فيديو", "Wan T2V");
        try
        {
            await using var workload = await workloadGate.AcquireAsync(WorkloadSlot.AiGeneration, projectId, ct);

            var project = await LoadProjectAsync(projectId, ct);
            if (string.IsNullOrWhiteSpace(project.ScriptJson))
                throw new InvalidOperationException("ولّد السكريبت أولاً.");

            var script = ScriptJsonReader.TryParse(project.ScriptJson)
                ?? throw new InvalidOperationException("بيانات السكريبت غير صالحة. أعد توليد السكريبت.");
            var plan = ProductionPlanResolver.Resolve(project, script);
            ScriptSegmentPlanner.EnsureScriptMatchesPlan(script, plan);
            project.ScriptJson = JsonSerializer.Serialize(script, LlmJsonParser.DefaultOptions);
            var suggestions = ScriptSegmentPlanner.SelectVideoSuggestions(script, plan, count);
            ValidateMotionPrompts(suggestions.Select(s => (s.SortOrder, s.UserMotionPrompt)));
            var segmentMs = ProductionPlanResolver.SegmentDurationMs(plan, script);

            if (replaceExisting)
                db.GeneratedImages.RemoveRange(project.GeneratedImages);

            var resolvedProvider = await ResolveVideoProviderAsync(provider, VideoProviderNames.WanT2v, ct);
            if (!VideoProviderNames.IsLocalComfy(resolvedProvider))
                throw new InvalidOperationException("توليد الفيديو يتطلب Wan محلياً عبر ComfyUI.");

            await comfyUi.EnsureAvailableAsync(ct);

            await using var gpu = await gpuOrchestrator.AcquireGpuAsync("Wan T2V", ct);

            var wanStyle = await settings.GetOrDefaultAsync(SettingKeys.WanVideoPrompt, "", ct);
            var positiveRules = await settings.GetOrDefaultAsync(
                SettingKeys.WanMotionPositivePrompt, WanMotionPromptDefaults.PositiveRules, ct);
            var configuredOutput = await settings.GetOrDefaultAsync(SettingKeys.OutputDirectory, "output", ct);
            var clipsDir = Path.Combine(paths.GetProjectDirectory(configuredOutput, projectId), "clips");
            Directory.CreateDirectory(clipsDir);

            var sortOrder = replaceExisting ? 1 : project.GeneratedImages.Count + 1;
            var clipCount = suggestions.Count;
            var clipIndex = 0;

            foreach (var suggestion in suggestions)
            {
                var videoPrompt = ImagePromptComposer.ComposeForWan(wanStyle, suggestion.UserMotionPrompt, positiveRules);
                var gpuPrepared = true;
                var clipPath = await comfyUi.GenerateWanTextToVideoAsync(
                    videoPrompt, clipsDir, sortOrder, project, ct, gpuPrepared);
                clipIndex++;

                if (clipIndex < clipCount)
                    await comfyUi.WaitComfyBatchContinuationAsync(ct);

                db.GeneratedImages.Add(new GeneratedImage
                {
                    VideoProjectId = projectId,
                    ScenePrompt = suggestion.NarrationExcerpt,
                    Prompt = videoPrompt,
                    LocalFilePath = $"{projectId:D}/clips/{Path.GetFileName(clipPath)}",
                    SortOrder = sortOrder,
                    AnimationMode = "static",
                    StartTimeMs = (sortOrder - 1) * segmentMs,
                    EndTimeMs = sortOrder * segmentMs
                });
                sortOrder++;
                project.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            project.VideoProviderUsed = resolvedProvider;
            project.ProductionWorkflowPath = nameof(ProductionWorkflowPath.TextToVideo);
            project.ImageProviderUsed = "WanT2V";
            project.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var dto = await ReloadDtoAsync(projectId, ct);
            activity.Complete(projectId, $"{suggestions.Count} مقطع فيديو");
            return dto;
        }
        catch (Exception ex)
        {
            activity.Fail(projectId, ex.Message);
            throw;
        }
    }

    private async Task EnsureVideoGenerationAllowedAsync(CancellationToken ct)
    {
        var profile = await DeploymentProfileSource.ResolveAsync(settings, ct);
        if (!DeploymentProfileResolver.SupportsVideoGeneration(profile))
            throw new InvalidOperationException("وضع أونلاين لا يدعم توليد الفيديو. استخدم مسار صورة إلى فيديو مع صور ثابتة.");
    }

    private static string ResolveUserMotion(ScriptOutputDto script, int sortOrder, bool isTextToVideo) =>
        MotionPromptHelper.ResolveEffective(script, sortOrder, isTextToVideo);

    private static void ValidateMotionPrompts(IEnumerable<(int SortOrder, string Motion)> items)
    {
        var missing = items
            .Where(i => string.IsNullOrWhiteSpace(i.Motion))
            .Select(i => i.SortOrder)
            .ToList();

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"اكتب وصف الحركة للمشاهد: {string.Join(", ", missing)} ثم احفظ.");
    }

    private async Task<VideoProject> LoadProjectAsync(Guid projectId, CancellationToken ct) =>
        await db.VideoProjects
            .Include(p => p.Category)
            .Include(p => p.GeneratedImages)
            .FirstAsync(p => p.Id == projectId, ct);

    private async Task<VideoProjectDto> ReloadDtoAsync(Guid projectId, CancellationToken ct)
    {
        var project = await db.VideoProjects
            .Include(p => p.Category)
            .Include(p => p.GeneratedImages)
            .AsNoTracking()
            .FirstAsync(p => p.Id == projectId, ct);
        return VideoProjectMapper.Map(project);
    }

    private async Task<string> ResolveVideoProviderAsync(
        string? requested, string endpointDefault, CancellationToken ct)
    {
        var configured = await settings.GetOrDefaultAsync(SettingKeys.VideoProvider, "", ct);
        var normalized = VideoProviderNames.Normalize(requested)
            ?? VideoProviderNames.Normalize(configured)
            ?? endpointDefault;

        if (!VideoProviderNames.IsSupported(normalized))
            throw new InvalidOperationException(
                $"مزود الفيديو '{normalized}' غير مدعوم. استخدم WanTI2V أو WanT2V.");

        return normalized;
    }
}
