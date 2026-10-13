using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Services;
using ContentEmpireSystem.Infrastructure.Services.Local;

namespace ContentEmpireSystem.Infrastructure.Services.Ai;
public class OpenAiClient(HttpClient http)
{
    public async Task<string> ChatAsync(
        string apiKey, string model, string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var request = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            response_format = new { type = "json_object" },
            temperature = 0.8
        };

        using var msg = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        msg.Headers.Add("Authorization", $"Bearer {apiKey}");
        msg.Content = JsonContent.Create(request);

        var response = await http.SendAsync(msg, ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
    }
}
