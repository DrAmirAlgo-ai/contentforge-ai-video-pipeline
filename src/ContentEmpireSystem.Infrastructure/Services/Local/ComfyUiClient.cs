using ContentEmpireSystem.Infrastructure.Services;
using ContentEmpireSystem.Infrastructure.Services.Ai;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Core.Entities;

namespace ContentEmpireSystem.Infrastructure.Services.Local;

public sealed class ComfyUiClient(
    IHttpClientFactory httpClientFactory,
    LocalEndpointResolver endpoints,
    ISystemSettingsService settings,
    Core.Interfaces.IOutputPathService outputPaths,
    LmStudioGpuService vramProbe)
{
    private static readonly string[] VideoExtensions = [".mp4", ".webm", ".mov", ".mkv"];
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    private sealed record ComfyOutputFile(string Filename, string Subfolder, string Type);

    public async Task<string> GenerateFluxImageAsync(
        string prompt, string outputDir, int index, CancellationToken ct,
        bool gpuAlreadyPrepared = false)
    {
        var workflowPath = await RequireWorkflowPathAsync(SettingKeys.LocalComfyWorkflowFlux, "FLUX", ct);
        var gen = await LoadFluxGenerationSettingsAsync(ct);
        var replacements = await BuildReplacementsAsync(
            outputDir, index, "scene", null, prompt, ct);
        replacements["{{STEPS}}"] = gen.FluxSteps.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ApplyFluxGenerationTokens(replacements, gen);

        return await RunWorkflowWithRetryAsync(
            workflowPath, replacements, outputDir, index, ImageExtensions,
            ComfyPipelineTimeouts.FluxWorkflowMinutes, ct, gpuAlreadyPrepared);
    }

    public async Task<string> GenerateWanImageToVideoAsync(
        string imagePath, string videoPrompt, string outputDir, int index,
        VideoProject? project = null, CancellationToken ct = default,
        bool gpuAlreadyPrepared = false)
    {
        var workflowPath = await ResolveWanTi2vWorkflowPathAsync(ct);
        var gen = await LoadWanGenerationSettingsAsync(project, ct);
        var client = CreateClient();
        var baseUrl = await endpoints.ResolveComfyUiBaseUrlAsync(ct);

        var serverImagePath = await ResolveServerImagePathAsync(client, baseUrl, imagePath, ct);
        var replacements = await BuildReplacementsAsync(
            outputDir, index, "clip", serverImagePath, videoPrompt, ct);
        ApplyWanGenerationTokens(replacements, gen);

        return await RunWorkflowWithRetryAsync(
            workflowPath, replacements, outputDir, index, VideoExtensions,
            ComfyPipelineTimeouts.WanTi2vWorkflowMinutes, ct, gpuAlreadyPrepared, deepGpuClean: !gpuAlreadyPrepared);
    }

    public async Task<string> GenerateWanTextToVideoAsync(
        string videoPrompt, string outputDir, int index,
        VideoProject? project = null, CancellationToken ct = default,
        bool gpuAlreadyPrepared = false)
    {
        var workflowPath = await RequireWorkflowPathAsync(SettingKeys.LocalComfyWorkflowWanT2v, "Wan T2V", ct);
        var gen = await LoadWanGenerationSettingsAsync(project, ct);
        var replacements = await BuildReplacementsAsync(
            outputDir, index, "clip", null, videoPrompt, ct);
        ApplyWanGenerationTokens(replacements, gen);

        return await RunWorkflowWithRetryAsync(
            workflowPath, replacements, outputDir, index, VideoExtensions,
            ComfyPipelineTimeouts.WanT2vWorkflowMinutes, ct, gpuAlreadyPrepared, deepGpuClean: !gpuAlreadyPrepared);
    }

    public async Task<bool> PingAsync(CancellationToken ct)
    {
        try
        {
            var baseUrl = await endpoints.ResolveComfyUiBaseUrlAsync(ct);
            var client = CreateClient();
            using var response = await client.GetAsync($"{baseUrl}/system_stats", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task EnsureAvailableAsync(CancellationToken ct = default)
    {
        if (await PingAsync(ct))
            return;

        throw new InvalidOperationException(
            "ComfyUI غير متاح — شغّل ComfyUI Desktop وتأكد أن http://127.0.0.1:8188 يفتح في المتصفح.");
    }

    /// <summary>Wait for ComfyUI queue to drain, then optionally free VRAM before a new engine loads.</summary>
    public async Task PrepareGpuForWorkloadAsync(CancellationToken ct = default) =>
        await PrepareGpuForWorkloadAsync(deepClean: false, skipVramFree: false, engineSwitch: false, ct);

    public async Task PrepareGpuForWorkloadAsync(bool deepClean, CancellationToken ct = default) =>
        await PrepareGpuForWorkloadAsync(deepClean, skipVramFree: false, engineSwitch: false, ct);

    public async Task PrepareGpuForWorkloadAsync(
        bool deepClean,
        bool skipVramFree,
        bool engineSwitch,
        CancellationToken ct = default)
    {
        await EnsureAvailableAsync(ct);
        await WaitUntilQueueIdleAsync(ct);

        if (skipVramFree)
            return;

        if (engineSwitch)
        {
            await PrepareEngineSwitchFromFluxToWanAsync(ct);
            return;
        }

        if (deepClean)
        {
            await Task.Delay(2000, ct);
            await FreeGpuMemoryDeepAsync(ct);
            await Task.Delay(1500, ct);
            await FreeGpuMemoryAsync(ct);
            await EnsureMinimumVramFreeAsync(ComfyVramProfiles.WanMinFreeBeforeStartMiB, ct);
        }
        else
        {
            await FreeGpuMemoryAsync(ct);
        }
    }

    /// <summary>Between jobs in the same GPU batch: drain queue + settle — never call /free while GGUF stays loaded.</summary>
    public async Task WaitComfyBatchContinuationAsync(CancellationToken ct = default) =>
        await WaitFluxBatchContinuationAsync(ct);

    /// <inheritdoc cref="WaitComfyBatchContinuationAsync"/>
    public async Task WaitFluxBatchContinuationAsync(CancellationToken ct = default)
    {
        await WaitUntilQueueIdleAsync(ct);
        var delayMs = await ParseInterImageDelayMsAsync(ct);
        if (delayMs > 0)
            await Task.Delay(delayMs, ct);
    }

    private async Task PrepareEngineSwitchFromFluxToWanAsync(CancellationToken ct)
    {
        await TryInterruptComfyAsync(ct);
        var delaySec = await ParseEngineSwitchDelaySecAsync(ct);
        if (delaySec > 0)
            await Task.Delay(TimeSpan.FromSeconds(delaySec), ct);

        await WaitUntilQueueIdleAsync(ct);
        await FreeGpuMemoryAsync(ct);
        await Task.Delay(1500, ct);

        var free = await vramProbe.TryGetComfyFreeVramMiBAsync(ct);
        if (free is null || free >= ComfyVramProfiles.WanMinFreeBeforeStartMiB)
            return;

        if (!await UseSafeVramFreeAsync(ct))
        {
            await FreeGpuMemoryDeepAsync(ct);
            await Task.Delay(2000, ct);
            free = await vramProbe.TryGetComfyFreeVramMiBAsync(ct);
        }

        if (free.HasValue && free.Value < ComfyVramProfiles.WanMinFreeBeforeStartMiB)
        {
            throw new InvalidOperationException(
                $"VRAM غير كافٍ لـ Wan ({free.Value} MB free، المطلوب {ComfyVramProfiles.WanMinFreeBeforeStartMiB} MB). " +
                "مع ComfyUI-GGUF على 8GB: أعد تشغيل ComfyUI يدوياً بعد توليد الصور وقبل تحريك Wan — " +
                "unload_models ينهار مع GGUF، وإعادة التشغيل هي الطريقة الآمنة لتفريغ VRAM.");
        }
    }

    private async Task TryInterruptComfyAsync(CancellationToken ct)
    {
        try
        {
            var baseUrl = await endpoints.ResolveComfyUiBaseUrlAsync(ct);
            var client = CreateClient();
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await client.PostAsync($"{baseUrl}/interrupt", content, ct);
            _ = response.IsSuccessStatusCode;
        }
        catch
        {
            // Best-effort.
        }
    }

    private async Task<int> ParseInterImageDelayMsAsync(CancellationToken ct)
    {
        var raw = await settings.GetOrDefaultAsync(
            SettingKeys.LocalComfyInterImageDelayMs,
            ComfyVramProfiles.FluxInterImageSettleMsDefault.ToString(),
            ct);
        return int.TryParse(raw, out var ms) ? Math.Clamp(ms, 0, 15000) : ComfyVramProfiles.FluxInterImageSettleMsDefault;
    }

    private async Task<int> ParseEngineSwitchDelaySecAsync(CancellationToken ct)
    {
        var raw = await settings.GetOrDefaultAsync(
            SettingKeys.LocalComfyEngineSwitchDelaySec,
            ComfyVramProfiles.EngineSwitchSettleSecDefault.ToString(),
            ct);
        return int.TryParse(raw, out var sec) ? Math.Clamp(sec, 2, 30) : ComfyVramProfiles.EngineSwitchSettleSecDefault;
    }

    private async Task EnsureMinimumVramFreeAsync(int minimumFreeMiB, CancellationToken ct)
    {
        if (!(await IsAutoFreeEnabledAsync(ct)))
            return;

        var free = await vramProbe.TryGetComfyFreeVramMiBAsync(ct);
        if (free is null || free >= minimumFreeMiB)
            return;

        await vramProbe.UnloadAllLoadedModelsAsync(ct);
        await Task.Delay(2000, ct);
        await FreeGpuMemoryAsync(ct);

        free = await vramProbe.TryGetComfyFreeVramMiBAsync(ct);
        if (free.HasValue && free.Value < minimumFreeMiB)
        {
            throw new InvalidOperationException(
                $"VRAM غير كافٍ ({free.Value} MB free، المطلوب {minimumFreeMiB} MB). " +
                "أعد تشغيل Comfy Desktop ثم حاول — على 8GB استخدم مشهد واحد أو قلّل مدة المشهد.");
        }
    }

    /// <summary>After FLUX/Wan batch items: skip /free so GGUF stays loaded. After final Wan job: soft free.</summary>
    public async Task ReleaseGpuAfterWorkloadAsync(
        bool fluxImageJob = false,
        bool keepModelLoaded = false,
        CancellationToken ct = default)
    {
        if (fluxImageJob || keepModelLoaded)
            return;

        if (!(await IsAutoFreeEnabledAsync(ct)))
            return;

        await Task.Delay(2000, ct);
        await FreeGpuMemoryAsync(ct);
    }

    public async Task WaitUntilQueueIdleAsync(CancellationToken ct = default)
    {
        var baseUrl = await endpoints.ResolveComfyUiBaseUrlAsync(ct);
        var client = CreateClient();
        var maxWaitSec = 300;
        var deadline = DateTime.UtcNow.AddSeconds(maxWaitSec);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            using var response = await client.GetAsync($"{baseUrl}/queue", ct);
            if (!response.IsSuccessStatusCode)
            {
                await Task.Delay(750, ct);
                continue;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var running = doc.RootElement.TryGetProperty("queue_running", out var runNode)
                ? runNode.GetArrayLength()
                : 0;
            var pending = doc.RootElement.TryGetProperty("queue_pending", out var pendNode)
                ? pendNode.GetArrayLength()
                : 0;
            if (running == 0 && pending == 0)
                return;

            await Task.Delay(750, ct);
        }

        throw new TimeoutException(
            $"ComfyUI queue still busy after {maxWaitSec}s. Wait for the current job to finish or restart ComfyUI.");
    }

    private enum ComfyFreeMode
    {
        /// <summary>free_memory only — avoids ComfyUI-GGUF unpatch crash on unload.</summary>
        Soft,
        /// <summary>unload_models + free_memory — only before Wan when VRAM is needed.</summary>
        Deep
    }

    public async Task FreeGpuMemoryAsync(CancellationToken ct = default) =>
        await FreeGpuMemoryAsync(ComfyFreeMode.Soft, ct);

    public async Task FreeGpuMemoryDeepAsync(CancellationToken ct = default) =>
        await FreeGpuMemoryAsync(ComfyFreeMode.Deep, ct);

    private async Task<bool> UseSafeVramFreeAsync(CancellationToken ct)
    {
        var raw = await settings.GetOrDefaultAsync(SettingKeys.LocalComfySafeVramFree, "true", ct);
        return !string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase);
    }

    private async Task FreeGpuMemoryAsync(ComfyFreeMode mode, CancellationToken ct)
    {
        if (!(await IsAutoFreeEnabledAsync(ct)))
            return;
        try
        {
            if (mode == ComfyFreeMode.Deep && await UseSafeVramFreeAsync(ct))
                mode = ComfyFreeMode.Soft;

            var baseUrl = await endpoints.ResolveComfyUiBaseUrlAsync(ct);
            var client = CreateClient();
            var json = mode == ComfyFreeMode.Deep
                ? """{"unload_models":true,"free_memory":true}"""
                : """{"free_memory":true}""";
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync($"{baseUrl}/free", content, ct);
            if (response.IsSuccessStatusCode)
                await Task.Delay(mode == ComfyFreeMode.Deep ? 1500 : 400, ct);
        }
        catch
        {
            // Best-effort — do not fail the pipeline if ComfyUI /free is unavailable.
        }
    }

    private async Task<Dictionary<string, string>> BuildReplacementsAsync(
        string laptopOutputDir,
        int index,
        string fileStem,
        string? serverImagePath,
        string prompt,
        CancellationToken ct)
    {
        // ComfyUI only accepts filename_prefix inside its own output folder.
        // Save under contentforge/… then download via /view to the project folder.
        var outputPrefix = await BuildOutputPrefixAsync(laptopOutputDir, fileStem, index, ct);

        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{{PROMPT}}"] = prompt,
            ["{{OUTPUT_PREFIX}}"] = outputPrefix.Replace('\\', '/'),
            ["{{SEED}}"] = Random.Shared.Next(1, int.MaxValue).ToString()
        };

        if (!string.IsNullOrWhiteSpace(serverImagePath))
        {
            var normalized = serverImagePath.Replace('\\', '/');
            replacements["{{IMAGE_PATH}}"] = normalized;
            replacements["{{IMAGE_FILE}}"] = Path.IsPathRooted(normalized)
                ? Path.GetFileName(normalized)
                : normalized;
        }

        return replacements;
    }

    private static void ApplyFluxGenerationTokens(
        Dictionary<string, string> replacements, LocalComfyGenerationSettings gen)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        replacements["{{WIDTH}}"] = gen.FluxWidth.ToString(inv);
        replacements["{{HEIGHT}}"] = gen.FluxHeight.ToString(inv);
    }

    private static void ApplyWanGenerationTokens(
        Dictionary<string, string> replacements, LocalComfyGenerationSettings gen)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        replacements["{{STEPS}}"] = gen.WanSteps.ToString(inv);
        replacements["{{CFG}}"] = gen.WanCfg.ToString("0.0", inv);
        replacements["{{DENOISE}}"] = gen.WanDenoise.ToString("0.0", inv);
        replacements["{{WIDTH}}"] = gen.WanWidth.ToString(inv);
        replacements["{{HEIGHT}}"] = gen.WanHeight.ToString(inv);
        replacements["{{LENGTH}}"] = gen.WanFrameCount.ToString(inv);
        replacements["{{NEGATIVE_PROMPT}}"] = gen.WanNegativePrompt;
        replacements["{{UNET_NAME}}"] = gen.WanUnetName;
        replacements["{{VAE_NAME}}"] = gen.WanVaeName;
    }

    private async Task<LocalComfyGenerationSettings> LoadWanGenerationSettingsAsync(
        VideoProject? project, CancellationToken ct)
    {
        var gen = await LocalComfyGenerationSettings.LoadAsync(settings, ct);
        var totalVram = await vramProbe.TryGetComfyTotalVramMiBAsync(ct);
        return LocalComfyGenerationSettings.ApplyVramProfile(gen, totalVram);
    }

    private async Task<LocalComfyGenerationSettings> LoadFluxGenerationSettingsAsync(CancellationToken ct)
    {
        var gen = await LocalComfyGenerationSettings.LoadAsync(settings, ct);
        var totalVram = await vramProbe.TryGetComfyTotalVramMiBAsync(ct);
        return LocalComfyGenerationSettings.ApplyVramProfile(gen, totalVram);
    }

    private async Task<string> BuildOutputPrefixAsync(
        string laptopOutputDir, string fileStem, int index, CancellationToken ct)
    {
        var configured = (await settings.GetOrDefaultAsync(
            SettingKeys.LocalComfyOutputPrefix, "contentforge", ct)).Trim();
        var outputRoot = NormalizeComfyFilenamePrefix(configured);
        var projectFolder = Path.GetFileName(laptopOutputDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(projectFolder))
            projectFolder = "project";

        return $"{outputRoot}/{projectFolder}/{fileStem}_{index:D2}";
    }

    private static string NormalizeComfyFilenamePrefix(string configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return "contentforge";

        var root = configured.Replace('\\', '/').Trim().Trim('/');
        if (root.Length == 0)
            return "contentforge";

        const string comfyOutputMarker = "/ComfyUI/output/";
        var markerIdx = root.IndexOf(comfyOutputMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIdx >= 0)
            root = root[(markerIdx + comfyOutputMarker.Length)..].Trim('/');

        if (root.StartsWith('/'))
        {
            var parts = root.Split('/', StringSplitOptions.RemoveEmptyEntries);
            root = parts.Length switch
            {
                0 => "contentforge",
                1 => parts[0] is "comfy-output" or "workspace" ? "contentforge" : parts[0],
                _ when parts[0].Equals("workspace", StringComparison.OrdinalIgnoreCase) &&
                       parts.Length > 1 &&
                       parts[1].Equals("comfy-output", StringComparison.OrdinalIgnoreCase)
                    => parts.Length > 2 ? string.Join('/', parts[2..]) : "contentforge",
                _ => string.Join('/', parts.Skip(1))
            };
        }

        if (root.Equals("comfy-output", StringComparison.OrdinalIgnoreCase))
            root = "contentforge";

        return string.IsNullOrWhiteSpace(root) ? "contentforge" : root.Trim('/');
    }

    private async Task<string> ResolveServerImagePathAsync(
        HttpClient client, string baseUrl, string localImagePath, CancellationToken ct)
    {
        var uploaded = await UploadImageToComfyAsync(client, baseUrl, localImagePath, ct);
        return string.IsNullOrEmpty(uploaded.Subfolder)
            ? uploaded.Name
            : $"{uploaded.Subfolder}/{uploaded.Name}";
    }

    private const int WorkflowMaxAttempts = 2;

    private async Task<string> RunWorkflowWithRetryAsync(
        string workflowPath,
        Dictionary<string, string> replacements,
        string outputDir,
        int index,
        string[] allowedExtensions,
        int defaultTimeoutMinutes,
        CancellationToken ct,
        bool gpuAlreadyPrepared = false,
        bool deepGpuClean = false)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= WorkflowMaxAttempts; attempt++)
        {
            try
            {
                if (!(gpuAlreadyPrepared && attempt == 1))
                {
                    await PrepareGpuForWorkloadAsync(
                        deepGpuClean || attempt > 1,
                        skipVramFree: gpuAlreadyPrepared && !deepGpuClean,
                        engineSwitch: false,
                        ct);
                }

                var result = await RunWorkflowAndCollectOutputAsync(
                    workflowPath, replacements, outputDir, index, allowedExtensions, defaultTimeoutMinutes, ct);
                var isFlux = allowedExtensions == ImageExtensions;
                await ReleaseGpuAfterWorkloadAsync(
                    fluxImageJob: isFlux,
                    keepModelLoaded: gpuAlreadyPrepared,
                    ct);
                return result;
            }
            catch (Exception ex) when (attempt < WorkflowMaxAttempts && IsRetriableComfyFailure(ex))
            {
                lastError = ex;
                if (!await PingAsync(ct))
                {
                    throw new InvalidOperationException(
                        "ComfyUI توقف فجأة أثناء التوليد (crash/VRAM). أعد تشغيل Comfy Desktop من التطبيق ثم أعد المحاولة.",
                        ex);
                }

                await FreeGpuMemoryAsync(ct);
                await Task.Delay(TimeSpan.FromSeconds(3 * attempt), ct);
            }
        }

        throw lastError ?? new InvalidOperationException("ComfyUI workflow failed after retries.");
    }

    private static bool IsRetriableComfyFailure(Exception ex) =>
        ex is TimeoutException
        || (ex is InvalidOperationException ioe
            && !ioe.Message.Contains("ComfyUI توقف", StringComparison.Ordinal)
            && (ioe.Message.Contains("502", StringComparison.Ordinal)
                || ioe.Message.Contains("503", StringComparison.Ordinal)
                || ioe.Message.Contains("504", StringComparison.Ordinal)
                || ioe.Message.Contains("hostbuf_file_reader_read failed", StringComparison.OrdinalIgnoreCase)));

    private static string FluxModelFailureHint(string detail)
    {
        if (!detail.Contains("hostbuf_file_reader_read failed", StringComparison.OrdinalIgnoreCase))
            return "";

        return " — ملف النموذج تالف أو ComfyUI لا يقرأه: أعد تشغيل ComfyUI وتأكد من flux1-schnell-Q4_K_S.gguf و clip_l.safetensors و t5xxl_fp8_e4m3fn.safetensors و ae.safetensors في مجلدات models.";
    }

    private async Task<string> RunWorkflowAndCollectOutputAsync(
        string workflowPath,
        Dictionary<string, string> replacements,
        string outputDir,
        int index,
        string[] allowedExtensions,
        int defaultTimeoutMinutes,
        CancellationToken ct)
    {
        var baseUrl = await endpoints.ResolveComfyUiBaseUrlAsync(ct);
        var client = CreateClient();

        var promptJson = await LoadWorkflowPromptAsync(workflowPath, replacements, ct);
        byte[] payloadBytes;
        try
        {
            payloadBytes = BuildPromptPayloadBytes(promptJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"فشل تحليل ملف workflow بعد استبدال المتغيرات ({workflowPath}). تأكد أن {{PROMPT}} و{{OUTPUT_PREFIX}} بين علامات اقتباس. التفاصيل: {ex.Message}",
                ex);
        }
        using var content = new ByteArrayContent(payloadBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        using var createResponse = await client.PostAsync($"{baseUrl}/prompt", content, ct);
        if (!createResponse.IsSuccessStatusCode)
        {
            var err = await createResponse.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"ComfyUI /prompt failed ({(int)createResponse.StatusCode}): {Truncate(err, 500)}");
        }

        using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync(ct));
        var promptId = createDoc.RootElement.GetProperty("prompt_id").GetString()
            ?? throw new InvalidOperationException("ComfyUI did not return prompt_id.");

        var timeoutMin = await ParseIntSettingAsync(
            SettingKeys.LocalComfyTimeoutMinutes, defaultTimeoutMinutes, 5, 180, ct);
        var pollMs = await ParseIntSettingAsync(SettingKeys.LocalComfyPollIntervalMs, 2500, 500, 15000, ct);
        var deadline = DateTime.UtcNow.AddMinutes(timeoutMin);
        var fileStem = allowedExtensions == ImageExtensions ? "scene" : "clip";

        var firstPoll = true;
        while (DateTime.UtcNow < deadline)
        {
            if (!firstPoll)
                await Task.Delay(pollMs, ct);
            firstPoll = false;

            if (!await PingAsync(ct))
            {
                throw new InvalidOperationException(
                    "ComfyUI توقف أثناء انتظار النتيجة (crash غالباً بسبب VRAM). أعد تشغيل Comfy Desktop.");
            }

            using var historyResponse = await client.GetAsync($"{baseUrl}/history/{promptId}", ct);
            if (!historyResponse.IsSuccessStatusCode)
                continue;

            var historyBody = await historyResponse.Content.ReadAsStringAsync(ct);
            if (!historyBody.Contains(promptId, StringComparison.Ordinal))
                continue;

            using var historyDoc = JsonDocument.Parse(historyBody);
            if (!historyDoc.RootElement.TryGetProperty(promptId, out var run))
                continue;

            if (run.TryGetProperty("status", out var statusNode) &&
                statusNode.TryGetProperty("status_str", out var statusStr) &&
                statusStr.GetString() is "error")
            {
                var detail = ExtractComfyErrorDetail(statusNode);
                detail += WanModelFailureHint(detail);
                detail += FluxModelFailureHint(detail);
                throw new InvalidOperationException(
                    $"ComfyUI workflow failed for prompt {promptId}.{detail}");
            }

            var outputFile = TryExtractOutputFile(run, allowedExtensions);
            if (outputFile is not null)
            {
                var downloaded = await DownloadViewToLocalAsync(
                    client, baseUrl, outputFile, outputDir, index, fileStem, allowedExtensions, ct);
                if (downloaded is not null)
                    return downloaded;
            }
        }

        throw new TimeoutException(
            $"ComfyUI timed out after {timeoutMin} minutes. Check workflow output paths and ComfyUI console.");
    }

    private async Task<(string Name, string Subfolder)> UploadImageToComfyAsync(
        HttpClient client, string baseUrl, string localImagePath, CancellationToken ct)
    {
        if (!File.Exists(localImagePath))
            throw new FileNotFoundException($"Image for Comfy upload not found: {localImagePath}");

        await using var fs = File.OpenRead(localImagePath);
        using var form = new MultipartFormDataContent();
        form.Add(new StreamContent(fs), "image", Path.GetFileName(localImagePath));
        form.Add(new StringContent("true"), "overwrite");

        using var response = await client.PostAsync($"{baseUrl}/upload/image", form, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"ComfyUI /upload/image failed ({(int)response.StatusCode}): {Truncate(err, 500)}");
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("ComfyUI upload did not return image name.");

        var subfolder = doc.RootElement.TryGetProperty("subfolder", out var sf) ? sf.GetString() ?? "" : "";
        return (name, subfolder);
    }

    private static async Task<string?> DownloadViewToLocalAsync(
        HttpClient client,
        string baseUrl,
        ComfyOutputFile file,
        string outputDir,
        int index,
        string fileStem,
        string[] allowedExtensions,
        CancellationToken ct)
    {
        var query = new List<string>
        {
            $"filename={Uri.EscapeDataString(file.Filename)}",
            $"type={Uri.EscapeDataString(file.Type)}"
        };
        if (!string.IsNullOrWhiteSpace(file.Subfolder))
            query.Add($"subfolder={Uri.EscapeDataString(file.Subfolder)}");

        var url = $"{baseUrl.TrimEnd('/')}/view?{string.Join("&", query)}";
        using var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var ext = Path.GetExtension(file.Filename);
        if (string.IsNullOrWhiteSpace(ext) || !allowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            ext = allowedExtensions[0];

        Directory.CreateDirectory(outputDir);
        var localPath = Path.Combine(outputDir, $"{fileStem}_{index:D2}{ext}");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var outFile = File.Create(localPath);
        await stream.CopyToAsync(outFile, ct);
        return localPath;
    }

    private async Task<string> LoadWorkflowPromptAsync(
        string workflowPath, Dictionary<string, string> replacements, CancellationToken ct)
    {
        if (!File.Exists(workflowPath))
            throw new FileNotFoundException($"ComfyUI workflow file not found: {workflowPath}");

        var text = StripUtf8Bom(await File.ReadAllTextAsync(workflowPath, ct));
        return ComfyWorkflowSubstitutor.Apply(text, replacements);
    }

    private async Task<string> ResolveWanTi2vWorkflowPathAsync(CancellationToken ct)
    {
        var totalVram = await vramProbe.TryGetComfyTotalVramMiBAsync(ct);
        var useTiled = MediaDurationHelper.ParseForceSync(
            await settings.GetOrDefaultAsync(SettingKeys.LocalWanTiledVaeDecode, "false", ct));
        useTiled = ComfyVramProfiles.ShouldUseLowVramWanWorkflow(totalVram, useTiled);

        if (useTiled)
        {
            var lowVramPath = outputPaths.ToAbsolutePath(WorkflowDefaults.WanTi2vLowVramRelative);
            if (File.Exists(lowVramPath))
                return lowVramPath;
        }

        return await RequireWorkflowPathAsync(SettingKeys.LocalComfyWorkflowWanTi2v, "Wan TI2V", ct);
    }

    private async Task<string> RequireWorkflowPathAsync(string settingKey, string label, CancellationToken ct)
    {
        var path = (await settings.GetOrDefaultAsync(settingKey, "", ct)).Trim();
        if (string.IsNullOrWhiteSpace(path)
            && WorkflowDefaults.All.TryGetValue(settingKey, out var fallback))
            path = fallback;

        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException(
                $"مسار workflow لـ {label} غير مُعد. ضعه في الإعدادات ({settingKey}) — ملف JSON بصيغة ComfyUI API.");

        path = outputPaths.ToAbsolutePath(path);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"ملف workflow لـ {label} غير موجود: {path}. تأكد من وجود deploy/workflows على هذا الجهاز أو حدّث المسار في الإعدادات.");

        var rawJson = StripUtf8Bom(await File.ReadAllTextAsync(path, ct));
        ComfyWorkflowValidator.ValidateOrThrow(rawJson, path);

        return path;
    }

    private static string StripUtf8Bom(string text) =>
        text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;

    private static byte[] BuildPromptPayloadBytes(string promptJson)
    {
        using var promptDoc = JsonDocument.Parse(promptJson);
        using var stream = new MemoryStream();
        if (promptDoc.RootElement.TryGetProperty("prompt", out var existingPrompt))
            JsonSerializer.Serialize(stream, existingPrompt);
        else
        {
            JsonSerializer.Serialize(
                stream,
                new Dictionary<string, JsonElement> { ["prompt"] = promptDoc.RootElement.Clone() });
        }

        var bytes = stream.ToArray();
        return HasUtf8Bom(bytes) ? bytes[3..] : bytes;
    }

    private static bool HasUtf8Bom(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

    private static string ExtractComfyErrorDetail(JsonElement statusNode)
    {
        if (!statusNode.TryGetProperty("messages", out var messages))
            return "";

        foreach (var item in messages.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 2)
                continue;

            var level = item[0].GetString();
            if (!string.Equals(level, "execution_error", StringComparison.OrdinalIgnoreCase))
                continue;

            if (item[1].ValueKind == JsonValueKind.Object &&
                item[1].TryGetProperty("exception_message", out var msg))
            {
                var text = msg.GetString();
                return string.IsNullOrWhiteSpace(text) ? "" : $" {text}";
            }
        }

        return "";
    }

    private static string WanModelFailureHint(string detail)
    {
        if (!detail.Contains("Could not detect model type", StringComparison.OrdinalIgnoreCase)
            && !detail.Contains("wan2", StringComparison.OrdinalIgnoreCase))
            return "";

        return " — تأكد من تثبيت نموذج Wan 2.2 TI2V في ComfyUI (wan2.2_ti2v_5B أو GGUF المكافئ).";
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient("ComfyUI");
        client.Timeout = TimeSpan.FromMinutes(60);
        return client;
    }

    private async Task<int> ParseIntSettingAsync(
        string key, int defaultValue, int min, int max, CancellationToken ct)
    {
        var raw = await settings.GetOrDefaultAsync(key, defaultValue.ToString(), ct);
        return !int.TryParse(raw, out var value) ? defaultValue : Math.Clamp(value, min, max);
    }

    private async Task<bool> IsAutoFreeEnabledAsync(CancellationToken ct)
    {
        var raw = await settings.GetOrDefaultAsync(SettingKeys.LocalComfyAutoFreeEnabled, "false", ct);
        return bool.TryParse(raw, out var enabled) && enabled;
    }

    private static ComfyOutputFile? TryExtractOutputFile(JsonElement run, string[] allowedExtensions)
    {
        if (!run.TryGetProperty("outputs", out var outputs))
            return null;

        foreach (var node in outputs.EnumerateObject())
        {
            if (!node.Value.TryGetProperty("images", out var media) &&
                !node.Value.TryGetProperty("gifs", out media) &&
                !node.Value.TryGetProperty("videos", out media))
                continue;

            foreach (var item in media.EnumerateArray())
            {
                if (!item.TryGetProperty("filename", out var fn))
                    continue;

                var name = fn.GetString();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var subfolder = item.TryGetProperty("subfolder", out var sf) ? sf.GetString() ?? "" : "";
                var type = item.TryGetProperty("type", out var t) ? t.GetString() ?? "output" : "output";

                if (!allowedExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) &&
                    type != "output")
                    continue;

                return new ComfyOutputFile(name, subfolder, type);
            }
        }

        return null;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";
}
