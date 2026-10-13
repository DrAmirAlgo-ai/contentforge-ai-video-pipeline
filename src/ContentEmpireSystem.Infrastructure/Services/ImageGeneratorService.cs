using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EdgeTTS.DotNet;
using EdgeTTS.DotNet.Models;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Entities;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Data;
using ContentEmpireSystem.Infrastructure.Services.Ai;
using ContentEmpireSystem.Infrastructure.Services.Local;
using Microsoft.EntityFrameworkCore;

namespace ContentEmpireSystem.Infrastructure.Services;

public class ImageGeneratorService(
    ApplicationDbContext db,
    ISystemSettingsService settings,
    IOutputPathService paths,
    IHttpClientFactory httpClientFactory,
    IWorkloadGate workloadGate,
    ComfyUiClient comfyUi,
    IComfyUiProcessManager comfyProcessManager,
    ILocalGpuOrchestrator gpuOrchestrator,
    SystemActivityService activity,
    IOperationFileLogger operationLog) : IImageGeneratorService
{
    private const string HuggingFaceFreeFluxModel = "black-forest-labs/FLUX.1-schnell";
    private const string HuggingFaceRouterInferenceBaseUrl = "https://router.huggingface.co/hf-inference/models/";
    private const string HuggingFaceFreeFluxEndpoint =
        $"{HuggingFaceRouterInferenceBaseUrl}{HuggingFaceFreeFluxModel}";
    private static readonly string[] HuggingFaceFreeFluxSpaceBaseUrls =
    [
        "https://evalstate-flux1-schnell.hf.space",
        "https://mombeeru-flux-1-schnell.hf.space",
        "https://black-forest-labs-flux-1-schnell.hf.space"
    ];

    private static readonly JsonSerializerOptions JsonOptions = LlmJsonParser.DefaultOptions;

    public async Task GenerateImagesForProjectAsync(Guid projectId, CancellationToken ct = default)
        => await GenerateImagesAsync(projectId, 0, false, provider: null, ct);

    public async Task<VideoProjectDto> GenerateImagesAsync(
        Guid projectId, int count, bool replaceExisting, string? provider = null, CancellationToken ct = default)
    {
        const string op = "توليد الصور (FLUX)";
        operationLog.LogStart(projectId, op);
        activity.Start(projectId, "توليد الصور", "FLUX / مزود الصور");
        try
        {
        await using var gate = await workloadGate.AcquireAsync(WorkloadSlot.AiGeneration, projectId, ct);
        var (resolvedProvider, apiKey) = await ResolveImageProviderAsync(provider, ct);
        var usesLocalGpu = ImageProviderNames.IsLocal(resolvedProvider);

        var project = await db.VideoProjects
            .Include(p => p.Category)
            .Include(p => p.GeneratedImages)
            .FirstAsync(p => p.Id == projectId, ct);

        if (string.IsNullOrWhiteSpace(project.ScriptJson))
            throw new InvalidOperationException("Generate script first.");

        var script = ScriptJsonReader.TryParse(project.ScriptJson)
            ?? throw new InvalidOperationException("بيانات السكريبت غير صالحة. أعد توليد السكريبت.");
        var plan = ProductionPlanResolver.Resolve(project, script);
        ScriptSegmentPlanner.EnsureScriptMatchesPlan(script, plan);
        project.ScriptJson = JsonSerializer.Serialize(script, JsonOptions);
        var suggestions = ScriptSegmentPlanner.SelectImageSuggestions(script, plan, count);
        var segmentMs = ProductionPlanResolver.SegmentDurationMs(plan, script);

        if (replaceExisting)
            db.GeneratedImages.RemoveRange(project.GeneratedImages);

        var basePrompt = await settings.GetOrDefaultAsync(SettingKeys.FluxImagePrompt, "", ct);
        project.ImageProviderUsed = resolvedProvider;

        var configuredOutput = await settings.GetOrDefaultAsync(SettingKeys.OutputDirectory, "output", ct);
        var projectDir = paths.GetProjectDirectory(configuredOutput, projectId);
        var imagesDir = Path.Combine(projectDir, "images");
        Directory.CreateDirectory(imagesDir);

        var startOrder = replaceExisting ? 1 : project.GeneratedImages.Count + 1;
        var sortOrder = startOrder;
        var total = suggestions.Count;

        IAsyncDisposable? gpuLease = null;
        if (usesLocalGpu)
            gpuLease = await gpuOrchestrator.AcquireGpuAsync("FLUX", ct);

        try
        {
        for (var i = 0; i < suggestions.Count; i++)
        {
            var suggestion = suggestions[i];
            activity.Progress(projectId, $"صورة {i + 1} من {total} ({resolvedProvider})");

            var sceneContent = ImagePromptComposer.ComposeSceneContent(suggestion);
            var apiPrompt = ImagePromptComposer.ComposeForGeneration(basePrompt, suggestion);
            var skipGpuPrep = usesLocalGpu;
            var (imageUrl, localPath) = await GenerateImageWithOptionalGpuAsync(
                usesLocalGpu, resolvedProvider, apiKey, apiPrompt, imagesDir, sortOrder, projectId, ct, skipGpuPrep);

            if (usesLocalGpu && i < suggestions.Count - 1)
                await comfyUi.WaitFluxBatchContinuationAsync(ct);

            operationLog.LogFileExchange(projectId, op, apiPrompt[..Math.Min(80, apiPrompt.Length)], localPath, $"scene {sortOrder}");

            db.GeneratedImages.Add(new GeneratedImage
            {
                VideoProjectId = projectId,
                ScenePrompt = sceneContent,
                Prompt = apiPrompt,
                ImageUrl = imageUrl,
                LocalFilePath = ToPortableScenePath(projectId, localPath),
                SortOrder = sortOrder,
                AnimationMode = "zoom_in",
                StartTimeMs = (sortOrder - 1) * segmentMs,
                EndTimeMs = sortOrder * segmentMs
            });
            sortOrder++;
            project.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        }
        finally
        {
            if (gpuLease is not null)
                await gpuLease.DisposeAsync();
        }

        if (usesLocalGpu
            && string.Equals(resolvedProvider, ImageProviderNames.ComfyFlux, StringComparison.OrdinalIgnoreCase))
        {
            activity.Progress(projectId, "إعادة تشغيل ComfyUI لتفريغ VRAM قبل Wan…");
            await comfyProcessManager.RestartAfterFluxBatchAsync(ct);
        }

        await MaybeApplyAutoMotionToProjectAsync(project, ct);
        await db.SaveChangesAsync(ct);

        if (usesLocalGpu
            && string.Equals(resolvedProvider, ImageProviderNames.ComfyFlux, StringComparison.OrdinalIgnoreCase)
            && !await comfyUi.PingAsync(ct))
        {
            activity.Progress(projectId, "تنبيه: ComfyUI توقف بعد توليد الصور — شغّله قبل تحريك Wan");
        }

        var dto = await ReloadProjectDto(projectId, ct);
        operationLog.LogComplete(projectId, op, $"{suggestions.Count} image(s)");
        activity.Complete(projectId, $"{suggestions.Count} صورة");
        return dto;
        }
        catch (Exception ex)
        {
            operationLog.LogFail(projectId, op, ex.Message, ex);
            activity.Fail(projectId, ex.Message);
            throw;
        }
    }

    public async Task<VideoProjectDto> RegenerateImageAsync(
        Guid projectId, int imageId, string? provider = null, CancellationToken ct = default)
    {
        await using var gate = await workloadGate.AcquireAsync(WorkloadSlot.AiGeneration, projectId, ct);
        var (resolvedProvider, apiKey) = await ResolveImageProviderAsync(provider, ct);
        var usesLocalGpu = ImageProviderNames.IsLocal(resolvedProvider);

        var image = await db.GeneratedImages
            .FirstOrDefaultAsync(i => i.Id == imageId && i.VideoProjectId == projectId, ct)
            ?? throw new InvalidOperationException("Image not found.");

        var configuredOutput = await settings.GetOrDefaultAsync(SettingKeys.OutputDirectory, "output", ct);
        var imagesDir = Path.Combine(paths.GetProjectDirectory(configuredOutput, projectId), "images");
        Directory.CreateDirectory(imagesDir);

        var basePrompt = await settings.GetOrDefaultAsync(SettingKeys.FluxImagePrompt, "", ct);
        var scenePrompt = !string.IsNullOrWhiteSpace(image.ScenePrompt)
            ? image.ScenePrompt
            : ImagePromptComposer.ExtractScenePrompt(image.Prompt, basePrompt);
        var apiPrompt = FluxPromptComposer.ComposeForGeneration(basePrompt, scenePrompt);
        image.ScenePrompt = scenePrompt;
        image.Prompt = apiPrompt;

        IAsyncDisposable? gpuLease = null;
        if (usesLocalGpu)
            gpuLease = await gpuOrchestrator.AcquireGpuAsync("FLUX", ct);

        try
        {
        var (url, localPath) = await GenerateImageWithOptionalGpuAsync(
            usesLocalGpu, resolvedProvider, apiKey, apiPrompt, imagesDir, image.SortOrder, projectId, ct,
            gpuAlreadyPrepared: usesLocalGpu);
        image.ImageUrl = url;
        image.LocalFilePath = ToPortableScenePath(projectId, localPath);
        }
        finally
        {
            if (gpuLease is not null)
                await gpuLease.DisposeAsync();
        }

        var project = await db.VideoProjects
            .Include(p => p.GeneratedImages)
            .FirstAsync(p => p.Id == projectId, ct);
        project.ImageProviderUsed = resolvedProvider;
        project.UpdatedAt = DateTime.UtcNow;
        await MaybeApplyAutoMotionToProjectAsync(project, ct);
        await db.SaveChangesAsync(ct);

        return await ReloadProjectDto(projectId, ct);
    }

    public async Task<VideoProjectDto> DeleteImageAsync(Guid projectId, int imageId, CancellationToken ct = default)
    {
        var image = await db.GeneratedImages
            .FirstOrDefaultAsync(i => i.Id == imageId && i.VideoProjectId == projectId, ct)
            ?? throw new InvalidOperationException("Image not found.");

        db.GeneratedImages.Remove(image);
        var project = await db.VideoProjects
            .Include(p => p.GeneratedImages)
            .FirstAsync(p => p.Id == projectId, ct);
        project.UpdatedAt = DateTime.UtcNow;
        await MaybeApplyAutoMotionToProjectAsync(project, ct);
        await db.SaveChangesAsync(ct);

        return await ReloadProjectDto(projectId, ct);
    }

    public async Task<VideoProjectDto> ReplaceImageAsync(
        Guid projectId, int imageId, Stream file, string fileName, CancellationToken ct = default)
    {
        var image = await db.GeneratedImages
            .FirstOrDefaultAsync(i => i.Id == imageId && i.VideoProjectId == projectId, ct)
            ?? throw new InvalidOperationException("Image not found.");

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            throw new InvalidOperationException("Unsupported image format.");

        var configuredOutput = await settings.GetOrDefaultAsync(SettingKeys.OutputDirectory, "output", ct);
        var imagesDir = Path.Combine(paths.GetProjectDirectory(configuredOutput, projectId), "images");
        Directory.CreateDirectory(imagesDir);

        var localPath = Path.Combine(imagesDir, $"scene_{image.SortOrder:D2}_replace{ext}");
        await using (var fs = File.Create(localPath))
            await file.CopyToAsync(fs, ct);

        image.LocalFilePath = ToPortableScenePath(projectId, localPath);
        image.ImageUrl = null;

        var project = await db.VideoProjects
            .Include(p => p.GeneratedImages)
            .FirstAsync(p => p.Id == projectId, ct);
        project.ImageProviderUsed = "Manual";
        project.UpdatedAt = DateTime.UtcNow;
        await MaybeApplyAutoMotionToProjectAsync(project, ct);
        await db.SaveChangesAsync(ct);

        return await ReloadProjectDto(projectId, ct);
    }

    public async Task<VideoProjectDto> SetImageAnimationAsync(
        Guid projectId, int imageId, string animationMode, double motionSpeed, double maxZoom, CancellationToken ct = default)
    {
        var image = await db.GeneratedImages
            .FirstOrDefaultAsync(i => i.Id == imageId && i.VideoProjectId == projectId, ct)
            ?? throw new InvalidOperationException("Image not found.");

        image.AnimationMode = ImageMotionHelper.NormalizeMode(animationMode);
        image.MotionSpeed = ImageMotionHelper.NormalizeMotionSpeed(motionSpeed);
        image.MaxZoom = ImageMotionHelper.NormalizeMaxZoom(maxZoom);

        var project = await db.VideoProjects.FirstAsync(p => p.Id == projectId, ct);
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return await ReloadProjectDto(projectId, ct);
    }

    public async Task<VideoProjectDto> SetBulkImageAnimationsAsync(
        Guid projectId,
        IReadOnlyList<(int ImageId, string AnimationMode, double MotionSpeed, double MaxZoom)> updates,
        CancellationToken ct = default)
    {
        if (updates.Count == 0)
            return await ReloadProjectDto(projectId, ct);

        var imageIds = updates.Select(u => u.ImageId).ToList();
        var images = await db.GeneratedImages
            .Where(i => i.VideoProjectId == projectId && imageIds.Contains(i.Id))
            .ToListAsync(ct);

        foreach (var (imageId, animationMode, motionSpeed, maxZoom) in updates)
        {
            var image = images.FirstOrDefault(i => i.Id == imageId);
            if (image is null) continue;

            image.AnimationMode = ImageMotionHelper.NormalizeMode(animationMode);
            image.MotionSpeed = ImageMotionHelper.NormalizeMotionSpeed(motionSpeed);
            image.MaxZoom = ImageMotionHelper.NormalizeMaxZoom(maxZoom);
        }

        var project = await db.VideoProjects.FirstAsync(p => p.Id == projectId, ct);
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return await ReloadProjectDto(projectId, ct);
    }

    public async Task<VideoProjectDto> ApplyAutoMotionAssignmentsAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await db.VideoProjects
            .Include(p => p.GeneratedImages)
            .FirstAsync(p => p.Id == projectId, ct);

        ImageMotionAutoAssigner.Apply(project.GeneratedImages);
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return await ReloadProjectDto(projectId, ct);
    }

    private async Task MaybeApplyAutoMotionToProjectAsync(VideoProject project, CancellationToken ct)
    {
        var mode = await settings.GetOrDefaultAsync(SettingKeys.ImageMotionAssignment, "auto", ct);
        if (!ImageMotionAutoAssigner.IsAutoMode(mode))
            return;

        if (project.GeneratedImages.Count == 0)
            await db.Entry(project).Collection(p => p.GeneratedImages).LoadAsync(ct);

        ImageMotionAutoAssigner.Apply(project.GeneratedImages);
    }

    private async Task<VideoProjectDto> ReloadProjectDto(Guid projectId, CancellationToken ct)
    {
        var project = await db.VideoProjects
            .Include(p => p.Category)
            .Include(p => p.GeneratedImages)
            .AsNoTracking()
            .FirstAsync(p => p.Id == projectId, ct);
        return VideoProjectMapper.Map(project);
    }

    private async Task<(string Provider, string ApiKey)> ResolveImageProviderAsync(
        string? requestedProvider, CancellationToken ct)
    {
        var profile = await DeploymentProfileSource.ResolveAsync(settings, ct);

        var normalized = ImageProviderNames.Normalize(requestedProvider)
            ?? ImageProviderNames.Normalize(
                await settings.GetOrDefaultAsync(SettingKeys.ImageProvider, "", ct))
            ?? DeploymentProfileResolver.DefaultImageProvider(profile);

        if (normalized is ImageProviderNames.HuggingFace)
        {
            var apiKey = await settings.GetOrDefaultAsync(SettingKeys.HuggingFaceApiKey, "", ct);
            return (ImageProviderNames.HuggingFace, apiKey.Trim());
        }

        if (normalized is ImageProviderNames.ComfyFlux)
            return (ImageProviderNames.ComfyFlux, "");

        throw new InvalidOperationException(
            $"مزود الصور '{requestedProvider}' غير مدعوم. استخدم HuggingFace أو ComfyFlux.");
    }

    private async Task<string> RequireApiKeyAsync(string settingKey, string label, CancellationToken ct)
    {
        if (!await settings.HasValueAsync(settingKey, ct))
            throw new InvalidOperationException($"مفتاح {label} غير مُعد. أضفه من الإعدادات.");

        return (await settings.GetAsync(settingKey, ct))!;
    }

    private async Task<(string? ImageUrl, string LocalPath)> GenerateImageWithOptionalGpuAsync(
        bool usesLocalGpu,
        string provider,
        string apiKey,
        string prompt,
        string outputDir,
        int index,
        Guid projectId,
        CancellationToken ct,
        bool gpuAlreadyPrepared = false)
    {
        if (!usesLocalGpu)
            return await GenerateImageAsync(provider, apiKey, prompt, outputDir, index, projectId, ct);

        return await GenerateImageAsync(provider, apiKey, prompt, outputDir, index, projectId, ct, gpuAlreadyPrepared);
    }

    private async Task<(string? ImageUrl, string LocalPath)> GenerateImageAsync(
        string provider, string apiKey, string prompt, string outputDir, int index, Guid projectId, CancellationToken ct,
        bool gpuAlreadyPrepared = false)
    {
        return provider switch
        {
            ImageProviderNames.HuggingFace =>
                (null, await GenerateViaHuggingFaceAsync(apiKey, prompt, outputDir, index, projectId, ct)),
            ImageProviderNames.ComfyFlux =>
                (null, await comfyUi.GenerateFluxImageAsync(prompt, outputDir, index, ct, gpuAlreadyPrepared)),
            _ => throw new InvalidOperationException(
                $"مزود الصور '{provider}' غير مدعوم. استخدم HuggingFace أو ComfyFlux.")
        };
    }

    private async Task<string> GenerateViaHuggingFaceAsync(
        string apiKey, string prompt, string outputDir, int index, Guid projectId, CancellationToken ct)
    {
        var localPath = Path.Combine(outputDir, $"scene_{index:D2}.png");

        var hasToken = !string.IsNullOrWhiteSpace(apiKey)
            && await settings.HasValueAsync(SettingKeys.HuggingFaceApiKey, ct);

        if (!hasToken)
        {
            var spacePath = await TryGenerateViaHuggingFaceFreeSpaceAsync(prompt, outputDir, index, projectId, ct);
            if (!string.IsNullOrWhiteSpace(spacePath))
                return spacePath;

            throw new InvalidOperationException(
                "فشل توليد الصورة عبر المسار المجاني (HF Space). انتظر دقيقة وأعد المحاولة، أو أضف توكن Hugging Face.");
        }

        using var client = httpClientFactory.CreateClient("HuggingFaceImage");
        var (configuredEndpoint, configuredModel) = await ResolveHuggingFaceImageRouteAsync(ct);
        var selectedEndpoint = configuredEndpoint;
        var freeSpaceFallbackAttempted = false;

        for (var attempt = 0; attempt < 30; attempt++)
        {
            using var request = BuildHuggingFaceImageRequest(apiKey, prompt, selectedEndpoint);
            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (HttpRequestException ex)
            {
                if (!freeSpaceFallbackAttempted)
                {
                    freeSpaceFallbackAttempted = true;
                    var spaceLocalPath = await TryGenerateViaHuggingFaceFreeSpaceAsync(
                        prompt, outputDir, index, projectId, ct);
                    if (!string.IsNullOrWhiteSpace(spaceLocalPath))
                        return spaceLocalPath;
                }

                throw new InvalidOperationException(
                    "تعذر الاتصال بـ Hugging Face Router. تحقق من الإنترنت والتوكن.",
                    ex);
            }

            using (response)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                if (response.IsSuccessStatusCode)
                {
                    if (TryWriteImageBytes(bytes, localPath))
                        return localPath;

                    var body = Encoding.UTF8.GetString(bytes);
                    if (TrySaveHuggingFaceImagePayload(body, localPath))
                        return localPath;

                    throw new InvalidOperationException(
                        $"Hugging Face returned success but no image bytes were found. Response: {Truncate(body, 500)}");
                }

                var errorBody = Encoding.UTF8.GetString(bytes);

                if (ShouldRetryHuggingFace(response.StatusCode, errorBody))
                {
                    await Task.Delay(TimeSpan.FromSeconds(ParseHuggingFaceRetryDelaySec(errorBody)), ct);
                    continue;
                }

                if (response.StatusCode is HttpStatusCode.PaymentRequired
                    or HttpStatusCode.Unauthorized
                    or HttpStatusCode.Forbidden
                    or HttpStatusCode.TooManyRequests
                    or HttpStatusCode.InternalServerError
                    or HttpStatusCode.BadGateway
                    or HttpStatusCode.ServiceUnavailable)
                {
                    if (!freeSpaceFallbackAttempted)
                    {
                        freeSpaceFallbackAttempted = true;
                        var spaceLocalPath = await TryGenerateViaHuggingFaceFreeSpaceAsync(
                            prompt, outputDir, index, projectId, ct);
                        if (!string.IsNullOrWhiteSpace(spaceLocalPath))
                            return spaceLocalPath;
                    }
                }

                if (response.StatusCode is HttpStatusCode.PaymentRequired
                    or HttpStatusCode.Unauthorized
                    or HttpStatusCode.Forbidden)
                {
                    throw new InvalidOperationException(
                        "توكن Hugging Face مرفوض أو منتهي. راجع التوكن والكوتا من huggingface.co/settings/tokens");
                }

                throw new InvalidOperationException(
                    "فشل Hugging Face Router بعد فشل المسار المجاني. جرّب مرة أخرى أو استخدم ComfyFlux.");
            }
        }

        throw new InvalidOperationException(
            "فشل Hugging Face بعد كل المحاولات. جرّب ComfyFlux أو أعد المحاولة.");
    }

    private async Task<string?> TryGenerateViaHuggingFaceFreeSpaceAsync(
        string prompt,
        string outputDir,
        int index,
        Guid projectId,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("HuggingFaceSpace");

        foreach (var spaceBaseUrl in HuggingFaceFreeFluxSpaceBaseUrls)
        {
            using var spaceCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            spaceCts.CancelAfter(TimeSpan.FromMinutes(4));

            try
            {
                activity.Progress(projectId, $"HF Space · تجهيز scene_{index:D2}...");
                var submitUrl = $"{spaceBaseUrl}/gradio_api/call/infer";
                using var submitReq = new HttpRequestMessage(HttpMethod.Post, submitUrl);
                submitReq.Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        data = new object[] { prompt, 0, true, 768, 1344, 4 }
                    }),
                    Encoding.UTF8,
                    "application/json");

                using var submitRes = await client.SendAsync(submitReq, spaceCts.Token);
                if (!submitRes.IsSuccessStatusCode)
                    continue;

                var submitBody = await submitRes.Content.ReadAsStringAsync(spaceCts.Token);
                using var submitDoc = JsonDocument.Parse(submitBody);
                if (!submitDoc.RootElement.TryGetProperty("event_id", out var eventIdProp))
                    continue;

                var eventId = eventIdProp.GetString();
                if (string.IsNullOrWhiteSpace(eventId))
                    continue;

                var pollUrl = $"{spaceBaseUrl}/gradio_api/call/infer/{eventId}";
                for (var i = 0; i < 90; i++)
                {
                    if (i > 0)
                        await Task.Delay(TimeSpan.FromSeconds(2), spaceCts.Token);

                    activity.Progress(projectId, $"HF Space · scene_{index:D2} · محاولة {i + 1}/90");

                    string? sseBody;
                    try
                    {
                        sseBody = await ReadGradioPollBodyAsync(client, pollUrl, spaceCts.Token);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(sseBody))
                        continue;

                    if (sseBody.Contains("event: error", StringComparison.OrdinalIgnoreCase))
                        break;

                    if (!sseBody.Contains("event: complete", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var imageUrl = ExtractImageUrlFromSse(sseBody, spaceBaseUrl);
                    if (string.IsNullOrWhiteSpace(imageUrl))
                        continue;

                    var bytes = await client.GetByteArrayAsync(imageUrl, spaceCts.Token);
                    var localPath = Path.Combine(outputDir, $"scene_{index:D2}.png");
                    await File.WriteAllBytesAsync(localPath, bytes, spaceCts.Token);
                    return localPath;
                }
            }
            catch (OperationCanceledException) when (spaceCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // Try next HF Space mirror.
            }
            catch (HttpRequestException)
            {
                // Try next HF Space mirror.
            }
        }

        return null;
    }

    private static async Task<string?> ReadGradioPollBodyAsync(
        HttpClient client, string pollUrl, CancellationToken ct)
    {
        using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        pollCts.CancelAfter(TimeSpan.FromSeconds(20));

        using var pollReq = new HttpRequestMessage(HttpMethod.Get, pollUrl);
        using var pollRes = await client.SendAsync(
            pollReq, HttpCompletionOption.ResponseHeadersRead, pollCts.Token);
        if (!pollRes.IsSuccessStatusCode)
            return null;

        await using var stream = await pollRes.Content.ReadAsStreamAsync(pollCts.Token);
        using var reader = new StreamReader(stream);

        var sb = new StringBuilder(512);
        while (true)
        {
            var line = await reader.ReadLineAsync(pollCts.Token);
            if (line is null)
                break;

            sb.AppendLine(line);
            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase) &&
                (line.Contains("complete", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("error", StringComparison.OrdinalIgnoreCase)))
                break;
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static string? ExtractImageUrlFromSse(string sseBody, string spaceBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(sseBody))
            return null;

        // Handles common Gradio payloads that include a direct file URL.
        var m = Regex.Match(
            sseBody,
            @"https?://[^\s""']+\.(png|jpg|jpeg|webp)(\?[^\s""']*)?",
            RegexOptions.IgnoreCase);
        if (m.Success)
            return m.Value;

        // Newer Space responses may return URL-like paths without image extensions.
        var genericUrl = Regex.Match(
            sseBody,
            @"https?://[^\s""']+",
            RegexOptions.IgnoreCase);
        if (genericUrl.Success)
            return genericUrl.Value;

        // Try to parse "data: {...}" lines and extract any string field that looks like a URL.
        foreach (var rawLine in sseBody.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!rawLine.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;

            var payload = rawLine["data:".Length..].Trim();
            if (payload.Length == 0 || payload == "[DONE]")
                continue;

            try
            {
                using var doc = JsonDocument.Parse(payload);
                var url = FindUrlInJson(doc.RootElement, spaceBaseUrl);
                if (!string.IsNullOrWhiteSpace(url))
                    return url;
            }
            catch (JsonException)
            {
                // Some data lines are plain text; ignore.
            }
        }

        // Some Spaces return relative file paths in plain SSE text.
        var relativeFile = Regex.Match(
            sseBody,
            @"(/gradio_api/file=[^\s""']+|/file=[^\s""']+)",
            RegexOptions.IgnoreCase);
        if (relativeFile.Success)
            return NormalizeUrl(relativeFile.Value, spaceBaseUrl);

        return null;
    }

    private static string? FindUrlInJson(JsonElement element, string spaceBaseUrl)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
            {
                var value = element.GetString();
                return NormalizeUrl(value, spaceBaseUrl);
            }
            case JsonValueKind.Array:
            {
                foreach (var item in element.EnumerateArray())
                {
                    var nested = FindUrlInJson(item, spaceBaseUrl);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }

                return null;
            }
            case JsonValueKind.Object:
            {
                foreach (var property in element.EnumerateObject())
                {
                    var nested = FindUrlInJson(property.Value, spaceBaseUrl);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }

                return null;
            }
            default:
                return null;
        }
    }

    private static string? NormalizeUrl(string? value, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            return absolute.ToString();

        if (value.StartsWith("/"))
            return $"{baseUrl.TrimEnd('/')}{value}";

        return null;
    }

    private HttpRequestMessage BuildHuggingFaceImageRequest(string? apiKey, string prompt, string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                $"رابط Hugging Face للصور غير صالح: '{endpoint}'. استخدم رابطاً يبدأ بـ https:// (مثال: {HuggingFaceFreeFluxEndpoint}).");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, uri);
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        request.Headers.TryAddWithoutValidation("Accept", "image/png");

        var payload = JsonSerializer.Serialize(new
        {
            inputs = prompt,
            parameters = new
            {
                width = 768,
                height = 1344,
                num_inference_steps = 4
            }
        });

        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        return request;
    }

    private async Task<(string Endpoint, string Model)> ResolveHuggingFaceImageRouteAsync(CancellationToken ct)
    {
        var configuredModel = await settings.GetOrDefaultAsync(
            SettingKeys.HuggingFaceImageModel, HuggingFaceFreeFluxModel, ct);
        if (string.IsNullOrWhiteSpace(configuredModel))
            configuredModel = HuggingFaceFreeFluxModel;
        configuredModel = configuredModel.Trim();

        // Guardrail: if video/queue model text leaked into image model setting, force free FLUX image model.
        if (LooksLikeVideoModel(configuredModel))
            configuredModel = HuggingFaceFreeFluxModel;

        var defaultEndpoint = HuggingFaceFreeFluxEndpoint;
        var configuredEndpoint = await settings.GetOrDefaultAsync(
            SettingKeys.HuggingFaceImageEndpoint, defaultEndpoint, ct);
        if (string.IsNullOrWhiteSpace(configuredEndpoint))
            configuredEndpoint = defaultEndpoint;
        configuredEndpoint = NormalizeHuggingFaceImageEndpoint(configuredEndpoint.Trim(), configuredModel);
        if (string.IsNullOrWhiteSpace(configuredEndpoint))
            configuredEndpoint = defaultEndpoint;

        if (!IsLikelyHuggingFaceImageEndpoint(configuredEndpoint))
            configuredEndpoint = HuggingFaceFreeFluxEndpoint;

        if (configuredEndpoint.Contains("api-inference.huggingface.co", StringComparison.OrdinalIgnoreCase))
            configuredEndpoint = $"{HuggingFaceRouterInferenceBaseUrl}{configuredModel}";

        return (configuredEndpoint, configuredModel);
    }

    private static string NormalizeHuggingFaceImageEndpoint(string endpoint, string model)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return endpoint;

        if (!endpoint.Contains("://", StringComparison.Ordinal))
        {
            var modelId = endpoint.Contains('/') ? endpoint.Trim('/') : model.Trim();
            return $"{HuggingFaceRouterInferenceBaseUrl}{modelId}";
        }

        if (endpoint.StartsWith("//", StringComparison.Ordinal))
            return "https:" + endpoint;

        return endpoint;
    }

    private static bool IsHuggingFaceNetworkError(HttpStatusCode statusCode, string body)
    {
        if (statusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
            return true;

        return body.Contains("no data of the requested type was found", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("name resolution", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("dns", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("socketexception", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyHuggingFaceImageEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return false;

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return false;

        var hostOk = uri.Host.Contains("api-inference.huggingface.co", StringComparison.OrdinalIgnoreCase) ||
                     uri.Host.Contains("router.huggingface.co", StringComparison.OrdinalIgnoreCase);
        if (!hostOk)
            return false;

        var path = uri.AbsolutePath;
        var looksLikeImageModelPath = path.Contains("/models/", StringComparison.OrdinalIgnoreCase);
        var looksLikeVideoPath = path.Contains("/fal-ai/", StringComparison.OrdinalIgnoreCase) ||
                                 path.Contains("text-to-video", StringComparison.OrdinalIgnoreCase) ||
                                 path.Contains("video", StringComparison.OrdinalIgnoreCase);

        return looksLikeImageModelPath && !looksLikeVideoPath;
    }

    private static bool LooksLikeVideoModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return false;

        return model.Contains("wan", StringComparison.OrdinalIgnoreCase) ||
               model.Contains("zeroscope", StringComparison.OrdinalIgnoreCase) ||
               model.Contains("svd", StringComparison.OrdinalIgnoreCase) ||
               model.Contains("text-to-video", StringComparison.OrdinalIgnoreCase) ||
               model.Contains("video", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldRetryHuggingFace(HttpStatusCode statusCode, string body) =>
        statusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests &&
        (body.Contains("loading", StringComparison.OrdinalIgnoreCase) ||
         body.Contains("rate", StringComparison.OrdinalIgnoreCase));

    private static int ParseHuggingFaceRetryDelaySec(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("estimated_time", out var estimatedTime))
                return Math.Clamp((int)Math.Ceiling(estimatedTime.GetDouble()), 2, 30);
        }
        catch (JsonException)
        {
            // Ignore malformed retry payloads.
        }

        return 5;
    }

    private static bool TryWriteImageBytes(byte[] bytes, string localPath)
    {
        if (!LooksLikeImageBytes(bytes))
            return false;

        File.WriteAllBytes(localPath, bytes);
        return true;
    }

    private static bool LooksLikeImageBytes(byte[] data)
    {
        if (data.Length < 4)
            return false;

        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return true;

        if (data[0] == 0xFF && data[1] == 0xD8)
            return true;

        return data.Length >= 12 &&
               Encoding.ASCII.GetString(data, 0, 4) == "RIFF" &&
               Encoding.ASCII.GetString(data, 8, 4) == "WEBP";
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";

    private static bool TrySaveHuggingFaceImagePayload(string body, string localPath)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (!property.Name.Contains("image", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var text = property.Value.GetString();
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    if (text.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                    {
                        var comma = text.IndexOf(',');
                        if (comma > 0)
                            text = text[(comma + 1)..];
                    }

                    try
                    {
                        File.WriteAllBytes(localPath, Convert.FromBase64String(text));
                        return true;
                    }
                    catch (FormatException)
                    {
                        // Not base64 â€” try next property.
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON â€” caller handles as error.
        }

        return false;
    }

    private static string CreatePlaceholderImage(string outputDir, int index, string label)
    {
        var path = Path.Combine(outputDir, $"scene_{index:D2}.png");
        // Minimal 1080x1920 PNG placeholder (1x1 scaled by FFmpeg) - write a tiny valid PNG
        if (!File.Exists(path))
        {
            var bmp = new byte[]
            {
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
                0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
                0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
                0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
                0x54, 0x08, 0xD7, 0x63, 0x60, 0x60, 0x60, 0x00,
                0x00, 0x00, 0x04, 0x00, 0x01, 0x27, 0x34, 0x27,
                0x0A, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
                0x44, 0xAE, 0x42, 0x60, 0x82
            };
            File.WriteAllBytes(path, bmp);
        }
        return path;
    }

    private static string ToPortableScenePath(Guid projectId, string localPath) =>
        $"{projectId:D}/images/{Path.GetFileName(localPath)}";
}

