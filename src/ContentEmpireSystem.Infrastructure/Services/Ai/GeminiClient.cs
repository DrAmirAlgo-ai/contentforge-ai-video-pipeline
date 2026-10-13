using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Services;
using ContentEmpireSystem.Infrastructure.Services.Local;

namespace ContentEmpireSystem.Infrastructure.Services.Ai;
public class GeminiClient(HttpClient http, ISystemSettingsService settings)
{
    private static readonly string[] FallbackModels =
    [
        "gemini-2.5-flash-lite",
        "gemini-2.5-flash",
        "gemini-2.0-flash-lite",
    ];

    public async Task<string> ChatAsync(string apiKey, string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var primary = await settings.GetOrDefaultAsync(SettingKeys.GeminiModel, "gemini-2.5-flash-lite", ct);
        var modelsToTry = new[] { primary }
            .Concat(FallbackModels.Where(m => m != primary))
            .Distinct()
            .ToList();

        string? lastError = null;

        foreach (var model in modelsToTry)
        {
            var (success, result, error) = await TryGenerateAsync(apiKey, model, systemPrompt, userPrompt, ct);
            if (success) return result!;
            lastError = $"[{model}] {error}";
        }

        throw new InvalidOperationException($"Gemini API error (all models failed): {lastError}");
    }

    private async Task<(bool Success, string? Result, string? Error)> TryGenerateAsync(
        string apiKey, string model, string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var url = string.IsNullOrWhiteSpace(apiKey)
            ? $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"
            : $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

        var payload = new
        {
            systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[] { new { parts = new[] { new { text = userPrompt } } } },
            generationConfig = new { responseMimeType = "application/json", temperature = 0.8 }
        };

        using var msg = new HttpRequestMessage(HttpMethod.Post, url);
        msg.Content = JsonContent.Create(payload);

        var response = await http.SendAsync(msg, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            return (false, null, body);

        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("candidates")[0]
            .GetProperty("content").GetProperty("parts")[0]
            .GetProperty("text").GetString() ?? "{}";

        return (true, text, null);
    }
}
