using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Services;

namespace ContentEmpireSystem.Infrastructure.Services.Ai;
public class OpenAiCompatibleClient(IHttpClientFactory httpFactory)
{
    public async Task<bool> PingAsync(string baseUrl, CancellationToken ct)
    {
        var trimmed = baseUrl.TrimEnd('/');

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));

                var client = httpFactory.CreateClient("AiHealth");
                var chatModels = await LmStudioModelDiscovery.ListChatModelIdsAsync(
                    client, trimmed, apiKey: null, timeoutCts.Token);
                if (chatModels.Count > 0)
                    return true;
            }
            catch
            {
                if (attempt == 2)
                    return false;
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }

        return false;
    }

    public async Task<string> ChatAsync(
        string baseUrl,
        string model,
        string systemPrompt,
        string userPrompt,
        string label,
        int? maxOutputTokens = null,
        CancellationToken ct = default)
    {
        var url = $"{baseUrl.TrimEnd('/')}/v1/chat/completions";
        var jsonSystemPrompt = systemPrompt
            + "\n\nIMPORTANT: Respond with valid JSON only. No markdown fences, no explanation, no thinking tags."
            + " Escape every double quote inside string values as \\\".";

        var client = httpFactory.CreateClient("AiScript");
        var discovered = await LmStudioModelDiscovery.ListChatModelIdsAsync(client, baseUrl, apiKey: null, ct);
        var candidates = LmStudioModelDiscovery.BuildModelCandidates(model, discovered);

        string? lastError = null;
        foreach (var candidate in candidates)
        {
            var (success, result, error) = await TryChatAsync(
                url, candidate, jsonSystemPrompt, userPrompt, apiKey: null, useJsonFormat: true, maxOutputTokens, ct);
            if (success)
                return NormalizeJson(result!);

            lastError = error;

            // Always fall back to a plain-text request: some LM Studio builds reject
            // response_format/json_schema, and the repair pipeline can recover the JSON.
            (success, result, error) = await TryChatAsync(
                url, candidate, jsonSystemPrompt, userPrompt, apiKey: null, useJsonFormat: false, maxOutputTokens, ct);
            if (success)
                return NormalizeJson(result!);

            lastError = error;
            if (error is not null && !error.Contains("400", StringComparison.Ordinal)
                                 && !error.Contains("404", StringComparison.Ordinal))
                break;
        }

        var noChatModel = discovered.Count == 0;
        var hint = noChatModel
            ? " Qwen غير محمّل في LM Studio — حمّل qwen/qwen3.5-9b وفعّل Local Server."
            : lastError?.Contains("502", StringComparison.Ordinal) == true
                || lastError?.Contains("timed out", StringComparison.OrdinalIgnoreCase) == true
                ? " LM Studio لا يرد — شغّل الخادم وحمّل الموديل."
                : " تأكد أن Api.LMStudio.Model يطابق id من /v1/models على الخادم.";
        throw new InvalidOperationException($"{label} error: {lastError}.{hint}");
    }

    private async Task<(bool Success, string? Result, string? Error)> TryChatAsync(
        string url,
        string model,
        string systemPrompt,
        string userPrompt,
        string? apiKey,
        bool useJsonFormat,
        int? maxOutputTokens,
        CancellationToken ct)
    {
        var messages = BuildChatMessages(systemPrompt, userPrompt, useQwenThinkingBypass: IsQwenModel(model));
        var maxTokens = maxOutputTokens ?? (IsQwenModel(model) ? 32768 : 16384);

        object request = useJsonFormat
            ? new
            {
                model,
                messages,
                stream = false,
                format = "json",
                response_format = new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = "structured_response",
                        strict = false,
                        schema = new { type = "object" }
                    }
                },
                temperature = 0.7,
                max_tokens = maxTokens,
                chat_template_kwargs = new { enable_thinking = false }
            }
            : new
            {
                model,
                messages,
                stream = false,
                temperature = 0.7,
                max_tokens = maxTokens,
                chat_template_kwargs = new { enable_thinking = false }
            };

        var client = httpFactory.CreateClient("AiScript");
        const int maxAttempts = 3;
        HttpResponseMessage? response = null;
        string? body = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var msg = new HttpRequestMessage(HttpMethod.Post, url);
            if (!string.IsNullOrWhiteSpace(apiKey))
                msg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            msg.Content = JsonContent.Create(request);

            try
            {
                response = await client.SendAsync(msg, ct);
            }
            catch (Exception ex)
            {
                if (attempt == maxAttempts)
                    return (false, null, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
                continue;
            }

            body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
                break;

            if (attempt < maxAttempts && IsTransientStatus(response.StatusCode))
            {
                response.Dispose();
                await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
                continue;
            }

            return (false, null, SummarizeErrorBody(response.StatusCode, body));
        }

        if (response is null || body is null)
            return (false, null, "No response from AI endpoint.");

        if (!response.IsSuccessStatusCode)
            return (false, null, SummarizeErrorBody(response.StatusCode, body));

        using var doc = JsonDocument.Parse(body);
        var choice = doc.RootElement.GetProperty("choices")[0];
        var text = choice.TryGetProperty("message", out var message)
            ? ExtractAssistantText(message)
            : "";
        if (string.IsNullOrWhiteSpace(text) && choice.TryGetProperty("text", out var legacyText))
            text = legacyText.GetString() ?? "";

        response.Dispose();

        // An empty body is never a usable JSON reply (common when Qwen "thinking"
        // eats the whole budget). Treat it as a failure so the caller retries.
        if (string.IsNullOrWhiteSpace(text))
            return (false, null, "empty response from model");

        return (true, text, null);
    }

    private static object[] BuildChatMessages(string systemPrompt, string userPrompt, bool useQwenThinkingBypass)
    {
        if (!useQwenThinkingBypass)
        {
            return
            [
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            ];
        }

        // Qwen3.x on LM Studio: thinking consumes max_tokens and leaves content empty/truncated.
        // Prefill assistant with an empty think block to skip reasoning (LM Studio workaround).
        return
        [
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userPrompt },
            new { role = "assistant", content = " \n" }
        ];
    }

    private static bool IsQwenModel(string model) =>
        model.Contains("qwen", StringComparison.OrdinalIgnoreCase);

    private static string ExtractAssistantText(JsonElement message)
    {
        var candidates = new List<string>();
        foreach (var key in new[] { "content", "reasoning_content", "reasoning" })
        {
            if (!message.TryGetProperty(key, out var node))
                continue;

            var extracted = CoalesceJsonText(node);
            if (!string.IsNullOrWhiteSpace(extracted))
                candidates.Add(extracted);
        }

        return SelectBestJsonCandidate(candidates) ?? "";
    }

    private static string? SelectBestJsonCandidate(IReadOnlyList<string> candidates)
    {
        string? best = null;
        var bestScore = -1;

        foreach (var raw in candidates)
        {
            foreach (var prepared in LlmJsonParser.AllRepairCandidates(raw))
            {
                var score = ScoreJsonCandidate(prepared);
                if (score <= bestScore)
                    continue;

                bestScore = score;
                best = prepared;
            }
        }

        return best;
    }

    private static int ScoreJsonCandidate(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.Contains('{', StringComparison.Ordinal))
            return 0;

        try
        {
            using var doc = JsonDocument.Parse(text);
            return 20_000 + text.Length;
        }
        catch
        {
            var score = text.Length;
            if (text.Contains("fullNarrationText", StringComparison.OrdinalIgnoreCase))
                score += 5000;
            if (text.Contains("imageSuggestions", StringComparison.OrdinalIgnoreCase))
                score += 2000;
            return score;
        }
    }

    private static string CoalesceJsonText(JsonElement node) =>
        node.ValueKind switch
        {
            JsonValueKind.String => node.GetString() ?? "",
            JsonValueKind.Array => string.Concat(node.EnumerateArray().Select(CoalesceJsonText)),
            _ => ""
        };

    private static bool IsTransientStatus(HttpStatusCode code) =>
        code is HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout
            or HttpStatusCode.RequestTimeout;

    private static string SummarizeErrorBody(HttpStatusCode code, string body)
    {
        if (body.Contains("502", StringComparison.Ordinal) || code == HttpStatusCode.BadGateway)
            return "error code: 502";
        if (body.Length > 200)
            return $"HTTP {(int)code}";
        return body;
    }

    private static string NormalizeJson(string text)
    {
        text = LlmResponseJsonHelper.StripThinkingBlocks(text.Trim());
        if (string.IsNullOrWhiteSpace(text))
            return "{}";

        return LlmJsonParser.Prepare(text);
    }
}
