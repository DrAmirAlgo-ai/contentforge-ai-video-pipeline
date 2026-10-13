using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Services.Local;

namespace ContentEmpireSystem.Infrastructure.Services.Ai;

public record ScriptProviderConfig(string Provider, string? ApiKey, string? BaseUrl, string? Model);

public class AiProviderResolver(ISystemSettingsService settings, LocalEndpointResolver endpoints)
{
    public async Task<ScriptProviderConfig> ResolveScriptProviderAsync(CancellationToken ct)
    {
        var preferred = (await settings.GetOrDefaultAsync(SettingKeys.ScriptProvider, "Ollama", ct))
            .Trim().ToLowerInvariant();

        return preferred switch
        {
            "openai" => await ResolveOpenAiAsync(ct),
            "gemini" => await ResolveGeminiAsync(ct),
            "lmstudio" or "lm_studio" or "lm studio" or "qwen" => await ResolveLmStudioAsync(ct),
            "auto" => await ResolveAutoAsync(ct),
            _ => await ResolveOllamaAsync(ct)
        };
    }

    private async Task<ScriptProviderConfig> ResolveAutoAsync(CancellationToken ct)
    {
        var lm = await ResolveLmStudioAsync(ct);
        if (!string.IsNullOrWhiteSpace(lm.BaseUrl))
            return lm;

        var ollama = await ResolveOllamaAsync(ct);
        if (!string.IsNullOrWhiteSpace(ollama.BaseUrl))
            return ollama;

        if (await settings.HasValueAsync(SettingKeys.OpenAiApiKey, ct))
            return await ResolveOpenAiAsync(ct);

        return await ResolveGeminiAsync(ct);
    }

    private async Task<ScriptProviderConfig> ResolveLmStudioAsync(CancellationToken ct) =>
        new(ScriptProviderNames.LmStudio, null,
            await endpoints.ResolveLmStudioBaseUrlAsync(ct),
            await settings.GetOrDefaultAsync(SettingKeys.LmStudioModel, "qwen3.5-9b", ct));

    private async Task<ScriptProviderConfig> ResolveOllamaAsync(CancellationToken ct) =>
        new("Ollama", null,
            await settings.GetOrDefaultAsync(SettingKeys.OllamaBaseUrl, "http://localhost:11434", ct),
            await settings.GetOrDefaultAsync(SettingKeys.OllamaModel, "qwen2.5:7b", ct));

    private async Task<ScriptProviderConfig> ResolveOpenAiAsync(CancellationToken ct)
    {
        if (!await settings.HasValueAsync(SettingKeys.OpenAiApiKey, ct))
            throw new InvalidOperationException("OpenAI API Key غير مُعد. أضف المفتاح في الإعدادات أو غيّر مزود AI إلى Ollama.");

        return new("OpenAI",
            (await settings.GetAsync(SettingKeys.OpenAiApiKey, ct))!,
            null,
            await settings.GetOrDefaultAsync(SettingKeys.OpenAiModel, "gpt-4o-mini", ct));
    }

    private async Task<ScriptProviderConfig> ResolveGeminiAsync(CancellationToken ct) =>
        new("Gemini",
            await settings.GetOrDefaultAsync(SettingKeys.GeminiApiKey, "", ct),
            null,
            await settings.GetOrDefaultAsync(SettingKeys.GeminiModel, "gemini-2.5-flash-lite", ct));

    public async Task<(string Provider, string ApiKey)> ResolveTtsProviderAsync(CancellationToken ct)
    {
        if (await settings.HasValueAsync(SettingKeys.ElevenLabsApiKey, ct))
            return ("ElevenLabs", (await settings.GetAsync(SettingKeys.ElevenLabsApiKey, ct))!);

        return ("EdgeTTS", "");
    }

    public async Task<(string Provider, string ApiKey)> ResolveImageProviderAsync(
        string? requestedProvider = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(requestedProvider))
        {
            var normalized = ImageProviderNames.Normalize(requestedProvider)!;
            return normalized switch
            {
                ImageProviderNames.HuggingFace => await ResolveHuggingFaceAsync(ct),
                ImageProviderNames.ComfyFlux => (ImageProviderNames.ComfyFlux, ""),
                ImageProviderNames.Unsloth => await ResolveUnslothAsync(ct),
                _ => throw new InvalidOperationException(
                    $"Unsupported image provider '{requestedProvider}'. Use HuggingFace, ComfyFlux, or Unsloth.")
            };
        }

        var preferred = ImageProviderNames.Normalize(
            await settings.GetOrDefaultAsync(SettingKeys.ImageProvider, "", ct));

        if (preferred is ImageProviderNames.ComfyFlux)
            return (ImageProviderNames.ComfyFlux, "");

        if (preferred is ImageProviderNames.Unsloth)
            return await ResolveUnslothAsync(ct);

        if (await settings.HasValueAsync(SettingKeys.HuggingFaceApiKey, ct))
            return await ResolveHuggingFaceAsync(ct);

        return (ImageProviderNames.ComfyFlux, "");
    }

    private async Task<(string Provider, string ApiKey)> ResolveHuggingFaceAsync(CancellationToken ct)
    {
        if (!await settings.HasValueAsync(SettingKeys.HuggingFaceApiKey, ct))
            throw new InvalidOperationException("Hugging Face API Key is not configured. Add it in Settings.");

        return ("HuggingFace", (await settings.GetAsync(SettingKeys.HuggingFaceApiKey, ct))!);
    }

    private async Task<(string Provider, string ApiKey)> ResolveUnslothAsync(CancellationToken ct)
    {
        // The key is optional: a local Unsloth server may not require one.
        var key = await settings.GetOrDefaultAsync(SettingKeys.UnslothApiKey, "", ct);
        return (ImageProviderNames.Unsloth, key ?? "");
    }
}
