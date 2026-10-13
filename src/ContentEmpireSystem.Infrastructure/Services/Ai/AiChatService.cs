using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Services;
using ContentEmpireSystem.Infrastructure.Services.Local;

namespace ContentEmpireSystem.Infrastructure.Services.Ai;
public class AiChatService(
    IHttpClientFactory httpFactory,
    AiProviderResolver resolver,
    ISystemSettingsService settings,
    ILocalGpuOrchestrator gpuOrchestrator)
{
    private readonly OpenAiClient _openAi = new(httpFactory.CreateClient("AiScript"));
    private readonly OpenAiCompatibleClient _openAiCompatible = new(httpFactory);
    private readonly GeminiClient _gemini = new(httpFactory.CreateClient("AiScript"), settings);

    public async Task<(string Json, string Provider)> GenerateJsonAsync(
        string userPrompt,
        string instructionsKey,
        string jsonSchemaKey,
        string? legacyCombinedKey = null,
        int? maxOutputTokens = null,
        CancellationToken ct = default)
    {
        var instructions = await settings.GetOrDefaultAsync(instructionsKey, "", ct);
        var jsonSchema = await settings.GetOrDefaultAsync(jsonSchemaKey, "", ct);

        if (string.IsNullOrWhiteSpace(instructions) && string.IsNullOrWhiteSpace(jsonSchema)
            && !string.IsNullOrWhiteSpace(legacyCombinedKey))
        {
            var legacy = await settings.GetOrDefaultAsync(legacyCombinedKey, "", ct);
            (instructions, jsonSchema) = JsonPromptComposer.Split(legacy);
        }

        var systemPrompt = JsonPromptComposer.Compose(instructions, jsonSchema);
        return await ExecuteChatAsync(systemPrompt, userPrompt, maxOutputTokens, ct);
    }

    /// <summary>Universal script engine â€” ignores dashboard Script System Prompt (niche/category mode).</summary>
    public Task<(string Json, string Provider)> GenerateJsonWithBuiltInAsync(
        string userPrompt, int? maxOutputTokens = null, CancellationToken ct = default) =>
        GenerateJsonWithSystemAsync(
            userPrompt, JsonPromptDefaults.ScriptInstructions, JsonPromptDefaults.ScriptJsonSchema, maxOutputTokens, ct);

    public Task<(string Json, string Provider)> GenerateJsonWithSystemAsync(
        string userPrompt, string instructions, string jsonSchema, int? maxOutputTokens = null, CancellationToken ct = default)
    {
        var systemPrompt = JsonPromptComposer.Compose(instructions, jsonSchema);
        return ExecuteChatAsync(systemPrompt, userPrompt, maxOutputTokens, ct);
    }

    private async Task<(string Json, string Provider)> ExecuteChatAsync(
        string systemPrompt, string userPrompt, int? maxOutputTokens, CancellationToken ct)
    {
        var config = await ResolveConfigForChatAsync(ct);
        var usesLocalLlmGpu = config.Provider is ScriptProviderNames.LmStudio or ScriptProviderNames.Ollama;

        await using var gpuLease = usesLocalLlmGpu
            ? await gpuOrchestrator.AcquireGpuAsync("Qwen", ct)
            : null;

        var json = config.Provider switch
        {
            ScriptProviderNames.OpenAi => await _openAi.ChatAsync(config.ApiKey!, config.Model!, systemPrompt, userPrompt, ct),
            ScriptProviderNames.Ollama => await _openAiCompatible.ChatAsync(
                config.BaseUrl!, config.Model!, systemPrompt, userPrompt, "Ollama", maxOutputTokens, ct),
            ScriptProviderNames.LmStudio => await _openAiCompatible.ChatAsync(
                config.BaseUrl!, config.Model!, systemPrompt, userPrompt, "LM Studio (Qwen)", maxOutputTokens, ct),
            _ => await _gemini.ChatAsync(config.ApiKey ?? "", systemPrompt, userPrompt, ct)
        };

        return (JsonResponseNormalizer.Normalize(json), config.Provider);
    }

    private async Task<ScriptProviderConfig> ResolveConfigForChatAsync(CancellationToken ct)
    {
        var config = await resolver.ResolveScriptProviderAsync(ct);

        if (config.Provider is not (ScriptProviderNames.LmStudio or ScriptProviderNames.Ollama)
            || string.IsNullOrWhiteSpace(config.BaseUrl))
            return config;

        return config;
    }

}

internal static class JsonResponseNormalizer
{
    public static string Normalize(string text)
    {
        text = LlmResponseJsonHelper.StripThinkingBlocks(text.Trim());
        return LlmJsonParser.Prepare(text);
    }
}

