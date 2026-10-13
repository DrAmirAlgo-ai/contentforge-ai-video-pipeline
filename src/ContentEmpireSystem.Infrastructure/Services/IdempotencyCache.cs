using System.Collections.Concurrent;

namespace ContentEmpireSystem.Infrastructure.Services;

public sealed record IdempotencyRecord(int StatusCode, string ContentType, byte[] Body, DateTime ExpiresAt);

public sealed class IdempotencyCache
{
    private readonly ConcurrentDictionary<string, IdempotencyRecord> _entries = new();
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);

    public bool TryGet(string key, out IdempotencyRecord? record)
    {
        if (_entries.TryGetValue(key, out var existing))
        {
            if (existing.ExpiresAt > DateTime.UtcNow)
            {
                record = existing;
                return true;
            }

            _entries.TryRemove(key, out _);
        }

        record = null;
        return false;
    }

    public void Store(string key, int statusCode, string contentType, byte[] body, TimeSpan? ttl = null)
    {
        var expires = DateTime.UtcNow.Add(ttl ?? DefaultTtl);
        _entries[key] = new IdempotencyRecord(statusCode, contentType, body, expires);
    }

    public static string BuildKey(string method, string path, string idempotencyKey) =>
        $"{method}:{path}:{idempotencyKey.Trim()}".ToLowerInvariant();
}
