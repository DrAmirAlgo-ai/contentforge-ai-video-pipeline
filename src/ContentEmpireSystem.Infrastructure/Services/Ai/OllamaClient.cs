using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Services;
using ContentEmpireSystem.Infrastructure.Services.Local;

namespace ContentEmpireSystem.Infrastructure.Services.Ai;
public class OllamaClient(HttpClient http)
{
    public async Task<string> ChatAsync(
        string baseUrl, string model, string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var url = $"{baseUrl.TrimEnd('/')}/v1/chat/completions";
        var jsonSystemPrompt = systemPrompt + "\n\nIMPORTANT: Respond with valid JSON only. No markdown fences, no explanation.";

        var (success, result, error) = await TryChatAsync(url, model, jsonSystemPrompt, userPrompt, useJsonFormat: true, ct);
        if (success) return NormalizeJson(result!);

        (success, result, error) = await TryChatAsync(url, model, jsonSystemPrompt, userPrompt, useJsonFormat: false, ct);
        if (success) return NormalizeJson(result!);

        throw new InvalidOperationException(
            $"Ollama error: {error}. تأكد أن Ollama شغّال (ollama serve) والموديل مُحمّل (ollama pull {model}).");
    }

    private async Task<(bool Success, string? Result, string? Error)> TryChatAsync(
        string url, string model, string systemPrompt, string userPrompt, bool useJsonFormat, CancellationToken ct)
    {
        object request = useJsonFormat
            ? new
            {
                model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                stream = false,
                format = "json",
                temperature = 0.8
            }
            : new
            {
                model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                stream = false,
                temperature = 0.8
            };

        using var msg = new HttpRequestMessage(HttpMethod.Post, url);
        msg.Content = JsonContent.Create(request);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(msg, ct);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return (false, null, body);

        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("content").GetString() ?? "{}";

        return (true, text, null);
    }

    private static string NormalizeJson(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```"))
        {
            var start = text.IndexOf('\n') + 1;
            var end = text.LastIndexOf("```", StringComparison.Ordinal);
            if (end > start)
                text = text[start..end].Trim();
        }

        return text;
    }
}
