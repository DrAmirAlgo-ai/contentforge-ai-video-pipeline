using System.Diagnostics;
using System.Text.Json;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Services.Ai;
using Microsoft.Extensions.Logging;

namespace ContentEmpireSystem.Infrastructure.Services.Local;

/// <summary>
/// Auto-connects to local Qwen via LM Studio (1234) or Ollama (11434).
/// Tries LM Studio first, then Ollama, optionally starts Ollama and pulls the model.
/// </summary>
public interface ILocalQwenConnector
{
    Task<LocalQwenConnection> ResolveLiveAsync(CancellationToken ct = default);
    LocalQwenConnection? GetCached();
}

public sealed record LocalQwenConnection(
    string Provider,
    string BaseUrl,
    string Model,
    string SourceLabel);

public sealed class LocalQwenConnector(
    ISystemSettingsService settings,
    LocalEndpointResolver endpoints,
    OpenAiCompatibleClient openAiCompatible,
    IHttpClientFactory httpClientFactory,
    ILogger<LocalQwenConnector> logger) : ILocalQwenConnector
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(20);
    private LocalQwenConnection? _cached;
    private DateTime _cachedAt = DateTime.MinValue;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    public LocalQwenConnection? GetCached() =>
        _cached is not null && DateTime.UtcNow - _cachedAt < CacheTtl ? _cached : null;

    public async Task<LocalQwenConnection> ResolveLiveAsync(CancellationToken ct = default)
    {
        var cached = GetCached();
        if (cached is not null && await PingAsync(cached, ct))
            return cached;

        await _connectLock.WaitAsync(ct);
        try
        {
            cached = GetCached();
            if (cached is not null && await PingAsync(cached, ct))
                return cached;

            var lmUrl = await endpoints.ResolveLmStudioBaseUrlAsync(ct);
            var lmModel = AppEnvironmentVariables.GetLmStudioModel()
                ?? await settings.GetOrDefaultAsync(SettingKeys.LmStudioModel, "qwen3.5-9b", ct);

            if (await openAiCompatible.PingAsync(lmUrl, ct))
            {
                var resolvedModel = await ResolveDiscoveredModelAsync(lmUrl, lmModel, ct);
                return Cache(new LocalQwenConnection(
                    ScriptProviderNames.LmStudio, lmUrl, resolvedModel, "LM Studio"));
            }

            var ollamaUrl = (await settings.GetOrDefaultAsync(
                SettingKeys.OllamaBaseUrl, "http://127.0.0.1:11434", ct)).Trim().TrimEnd('/');
            var ollamaModel = await settings.GetOrDefaultAsync(
                SettingKeys.OllamaModel, "qwen2.5:7b", ct);

            if (await PingOllamaAsync(ollamaUrl, ct))
            {
                await EnsureOllamaModelAsync(ollamaUrl, ollamaModel, ct);
                return Cache(new LocalQwenConnection(
                    ScriptProviderNames.Ollama, ollamaUrl, ollamaModel, "Ollama (auto)"));
            }

            logger.LogInformation("Local Qwen not reachable — trying to start Ollama...");
            await TryStartOllamaAsync(ct);
            await Task.Delay(2500, ct);

            if (await PingOllamaAsync(ollamaUrl, ct))
            {
                await EnsureOllamaModelAsync(ollamaUrl, ollamaModel, ct);
                return Cache(new LocalQwenConnection(
                    ScriptProviderNames.Ollama, ollamaUrl, ollamaModel, "Ollama (started)"));
            }

            throw new InvalidOperationException(
                "Qwen غير متصل — شغّل LM Studio (Local Server + تحميل Qwen) أو ثبّت Ollama. " +
                $"جرب: LM Studio → {lmUrl} | Ollama → {ollamaUrl}");
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private LocalQwenConnection Cache(LocalQwenConnection connection)
    {
        _cached = connection;
        _cachedAt = DateTime.UtcNow;
        logger.LogInformation(
            "Qwen connected via {Source} at {Url} model={Model}",
            connection.SourceLabel, connection.BaseUrl, connection.Model);
        return connection;
    }

    private async Task<bool> PingAsync(LocalQwenConnection conn, CancellationToken ct) =>
        conn.Provider == ScriptProviderNames.Ollama
            ? await PingOllamaAsync(conn.BaseUrl, ct)
            : await openAiCompatible.PingAsync(conn.BaseUrl, ct);

    private async Task<string> ResolveDiscoveredModelAsync(string baseUrl, string configured, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("AiHealth");
        var discovered = await LmStudioModelDiscovery.ListChatModelIdsAsync(client, baseUrl, apiKey: null, ct);
        var candidates = LmStudioModelDiscovery.BuildModelCandidates(configured, discovered);
        return candidates.FirstOrDefault() ?? configured;
    }

    private async Task<bool> PingOllamaAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient("AiHealth");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await client.GetAsync($"{baseUrl}/api/tags", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task EnsureOllamaModelAsync(string baseUrl, string model, CancellationToken ct)
    {
        if (await HasOllamaModelAsync(baseUrl, model, ct))
            return;

        logger.LogInformation("Pulling Ollama model {Model}...", model);
        await RunOllamaCliAsync($"pull {model}", TimeSpan.FromMinutes(30), ct);
    }

    private async Task<bool> HasOllamaModelAsync(string baseUrl, string model, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient("AiHealth");
            using var response = await client.GetAsync($"{baseUrl}/api/tags", ct);
            if (!response.IsSuccessStatusCode)
                return false;

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("models", out var models))
                return false;

            foreach (var item in models.EnumerateArray())
            {
                if (!item.TryGetProperty("name", out var nameNode))
                    continue;

                var name = nameNode.GetString() ?? "";
                if (name.Equals(model, StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith($"{model}:", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            // Fall through to pull attempt.
        }

        return false;
    }

    private static string? ResolveOllamaExe()
    {
        if (OperatingSystem.IsWindows())
        {
            var local = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Ollama", "ollama.exe");
            if (File.Exists(local))
                return local;
        }

        return null;
    }

    private static async Task TryStartOllamaAsync(CancellationToken ct)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var appExe = Path.Combine(localApp, "Programs", "Ollama", "ollama app.exe");
                if (File.Exists(appExe))
                {
                    Process.Start(new ProcessStartInfo(appExe) { UseShellExecute = true });
                    await Task.Delay(8000, ct);
                    return;
                }
            }

            var cli = ResolveOllamaExe();
            if (cli is not null)
                await RunOllamaCliAsync(cli, "serve", TimeSpan.FromSeconds(3), ct);
        }
        catch
        {
            // Best-effort — user may start Ollama manually.
        }
    }

    private static async Task RunOllamaCliAsync(string arguments, TimeSpan maxWait, CancellationToken ct)
    {
        var exe = ResolveOllamaExe() ?? "ollama";
        await RunOllamaCliAsync(exe, arguments, maxWait, ct);
    }

    private static async Task RunOllamaCliAsync(string exe, string arguments, TimeSpan maxWait, CancellationToken ct)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });

        if (process is null)
            return;

        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        waitCts.CancelAfter(maxWait);
        try
        {
            await process.WaitForExitAsync(waitCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // serve runs forever — expected for short timeout.
        }
    }
}
