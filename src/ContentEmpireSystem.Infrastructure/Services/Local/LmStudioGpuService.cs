using System.Net.Http.Json;
using System.Text.Json;
using ContentEmpireSystem.Core.Interfaces;

namespace ContentEmpireSystem.Infrastructure.Services.Local;

/// <summary>
/// Frees LM Studio GPU memory before ComfyUI (FLUX/Wan) workloads.
/// Qwen stays loaded on GPU after chat unless explicitly unloaded.
/// </summary>
public sealed class LmStudioGpuService(
    IHttpClientFactory httpClientFactory,
    LocalEndpointResolver endpoints)
{
    public async Task UnloadAllLoadedModelsAsync(CancellationToken ct = default)
    {
        await UnloadLmStudioModelsAsync(ct);
        await UnloadOllamaModelsAsync(ct);
    }

    private async Task UnloadLmStudioModelsAsync(CancellationToken ct)
    {
        var baseUrl = (await endpoints.ResolveLmStudioBaseUrlAsync(ct)).TrimEnd('/');
        var client = httpClientFactory.CreateClient("LMStudio");

        try
        {
            using var listResponse = await client.GetAsync($"{baseUrl}/api/v1/models", ct);
            if (!listResponse.IsSuccessStatusCode)
                return;

            using var doc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("models", out var models))
                return;

            foreach (var model in models.EnumerateArray())
            {
                if (!model.TryGetProperty("loaded_instances", out var instances))
                    continue;

                foreach (var instance in instances.EnumerateArray())
                {
                    if (!instance.TryGetProperty("id", out var idNode))
                        continue;

                    var instanceId = idNode.GetString();
                    if (string.IsNullOrWhiteSpace(instanceId))
                        continue;

                    using var unloadContent = JsonContent.Create(new { instance_id = instanceId });
                    using var unloadResponse = await client.PostAsync(
                        $"{baseUrl}/api/v1/models/unload", unloadContent, ct);
                    _ = unloadResponse.IsSuccessStatusCode;
                }
            }
        }
        catch
        {
            // Best-effort — LM Studio may be offline or on old API.
        }
    }

    private async Task UnloadOllamaModelsAsync(CancellationToken ct)
    {
        try
        {
            var baseUrl = "http://127.0.0.1:11434";
            var client = httpClientFactory.CreateClient("AiHealth");
            using var psResponse = await client.GetAsync($"{baseUrl}/api/ps", ct);
            if (!psResponse.IsSuccessStatusCode)
                return;

            using var doc = JsonDocument.Parse(await psResponse.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("models", out var models))
                return;

            foreach (var model in models.EnumerateArray())
            {
                if (!model.TryGetProperty("name", out var nameNode))
                    continue;

                var name = nameNode.GetString();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                using var content = JsonContent.Create(new { model = name, keep_alive = 0 });
                using var unload = await client.PostAsync($"{baseUrl}/api/generate", content, ct);
                _ = unload.IsSuccessStatusCode;
            }
        }
        catch
        {
            // Best-effort — Ollama may be offline.
        }
    }

    public async Task WaitForGpuReleaseAsync(
        int minimumFreeMiB,
        int maxWaitSeconds = 45,
        CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddSeconds(maxWaitSeconds);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var freeMiB = await TryGetComfyFreeVramMiBAsync(ct);
            if (freeMiB is null || freeMiB >= minimumFreeMiB)
                return;

            await UnloadAllLoadedModelsAsync(ct);
            await Task.Delay(1500, ct);
        }

        var finalFree = await TryGetComfyFreeVramMiBAsync(ct);
        if (finalFree.HasValue && finalFree.Value < minimumFreeMiB)
        {
            throw new InvalidOperationException(
                $"VRAM غير كافٍ لتشغيل ComfyUI ({finalFree.Value} MB free، المطلوب {minimumFreeMiB} MB). " +
                "أغلق LM Studio أو Comfy Desktop وأعد تشغيلهما، ثم حاول مرة أخرى — بدون تقليل الجودة.");
        }
    }

    public async Task<int?> TryGetComfyFreeVramMiBAsync(CancellationToken ct = default) =>
        await ReadComfyVramMiBAsync(useFree: true, ct);

    public async Task<int?> TryGetComfyTotalVramMiBAsync(CancellationToken ct = default) =>
        await ReadComfyVramMiBAsync(useFree: false, ct);

    private async Task<int?> ReadComfyVramMiBAsync(bool useFree, CancellationToken ct)
    {
        try
        {
            var comfyUrl = (await endpoints.ResolveComfyUiBaseUrlAsync(ct)).TrimEnd('/');
            var client = httpClientFactory.CreateClient("ComfyUI");
            using var response = await client.GetAsync($"{comfyUrl}/system_stats", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("devices", out var devices))
                return null;

            var field = useFree ? "vram_free" : "vram_total";
            long bytes = 0;
            foreach (var device in devices.EnumerateArray())
            {
                if (device.TryGetProperty(field, out var node) && node.TryGetInt64(out var value))
                    bytes += value;
            }

            return bytes > 0 ? (int)(bytes / (1024 * 1024)) : null;
        }
        catch
        {
            return null;
        }
    }
}
