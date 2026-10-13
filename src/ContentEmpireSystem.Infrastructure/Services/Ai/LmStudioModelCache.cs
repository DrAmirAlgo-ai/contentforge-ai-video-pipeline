using System.Collections.Concurrent;

namespace ContentEmpireSystem.Infrastructure.Services.Ai;

internal static class LmStudioModelCache
{
    private static readonly ConcurrentDictionary<string, CacheEntry> Entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(3);

    public static bool TryGet(string baseUrl, out IReadOnlyList<string> modelIds)
    {
        modelIds = Array.Empty<string>();
        if (!Entries.TryGetValue(Normalize(baseUrl), out var entry))
            return false;

        if (DateTime.UtcNow - entry.CachedAt > Ttl)
        {
            Entries.TryRemove(Normalize(baseUrl), out _);
            return false;
        }

        modelIds = entry.ModelIds;
        return true;
    }

    public static void Set(string baseUrl, IReadOnlyList<string> modelIds) =>
        Entries[Normalize(baseUrl)] = new CacheEntry(modelIds, DateTime.UtcNow);

    private static string Normalize(string baseUrl) => baseUrl.TrimEnd('/').ToLowerInvariant();

    private sealed record CacheEntry(IReadOnlyList<string> ModelIds, DateTime CachedAt);
}
