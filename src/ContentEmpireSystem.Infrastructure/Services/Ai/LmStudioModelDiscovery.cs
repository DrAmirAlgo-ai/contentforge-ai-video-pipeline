using System.Net.Http.Headers;
using System.Text.Json;

namespace ContentEmpireSystem.Infrastructure.Services.Ai;

internal static class LmStudioModelDiscovery
{
    public static bool IsEmbeddingModel(string modelId) =>
        modelId.Contains("embed", StringComparison.OrdinalIgnoreCase);

    public static async Task<IReadOnlyList<string>> ListChatModelIdsAsync(
        HttpClient client,
        string baseUrl,
        string? apiKey,
        CancellationToken ct)
    {
        if (LmStudioModelCache.TryGet(baseUrl, out var cached))
            return cached;

        var url = $"{baseUrl.TrimEnd('/')}/v1/models";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return Array.Empty<string>();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var ids = new List<string>();
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idNode))
                continue;

            var id = idNode.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(id) || IsEmbeddingModel(id))
                continue;

            ids.Add(id);
        }

        LmStudioModelCache.Set(baseUrl, ids);
        return ids;
    }

    public static IReadOnlyList<string> BuildModelCandidates(string configured, IReadOnlyList<string> discovered)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string>();

        void Add(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;
            id = id.Trim();
            if (seen.Add(id))
                candidates.Add(id);
        }

        var trimmed = configured.Trim();
        Add(trimmed);
        if (trimmed.StartsWith("qwen/", StringComparison.OrdinalIgnoreCase))
            Add(trimmed["qwen/".Length..]);
        else if (!string.IsNullOrWhiteSpace(trimmed))
            Add($"qwen/{trimmed}");

        foreach (var id in discovered)
            Add(id);

        return candidates;
    }
}
