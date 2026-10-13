using System.Collections.Concurrent;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContentEmpireSystem.Infrastructure.Services;

public class SystemSettingsService(ApplicationDbContext db) : ISystemSettingsService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, (string? Value, DateTime ExpiresAt)> _cache = new(StringComparer.Ordinal);

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        if (TryGetCached(key, out var cached))
            return cached;

        var setting = await db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, ct);
        var value = setting?.Value;
        SetCached(key, value);
        return value;
    }

    public async Task<string> GetOrDefaultAsync(string key, string defaultValue, CancellationToken ct = default)
    {
        var value = await GetAsync(key, ct);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    public async Task<bool> HasValueAsync(string key, CancellationToken ct = default)
    {
        var value = await GetAsync(key, ct);
        return !string.IsNullOrWhiteSpace(value);
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null)
        {
            db.SystemSettings.Add(new Core.Entities.SystemSetting
            {
                Key = key,
                Value = value,
                Category = "Custom",
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        InvalidateCache(key);
    }

    public async Task<Dictionary<string, string>> GetCategoryAsync(string category, CancellationToken ct = default)
    {
        return await db.SystemSettings.AsNoTracking()
            .Where(s => s.Category == category)
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsDictionaryAsync(CancellationToken ct = default) =>
        await db.SystemSettings.AsNoTracking()
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

    public async Task<List<SettingItemDto>> GetAllForAdminAsync(CancellationToken ct = default) =>
        await db.SystemSettings.AsNoTracking()
            .Where(s => !EF.Functions.Like(s.Key, "%Telegram%"))
            .OrderBy(s => s.Category).ThenBy(s => s.Key)
            .Select(s => new SettingItemDto
            {
                Id = s.Id,
                Key = s.Key,
                Value = s.IsSecret ? "" : s.Value,
                Category = s.Category,
                Description = s.Description,
                IsSecret = s.IsSecret,
                HasValue = s.IsSecret && !string.IsNullOrWhiteSpace(s.Value)
            })
            .ToListAsync(ct);

    public async Task<SettingsGroupDto> GetGroupedAsync(CancellationToken ct = default)
    {
        var all = await db.SystemSettings.AsNoTracking().ToListAsync(ct);
        return new SettingsGroupDto
        {
            ApiKeys = all.Where(s => s.Category == "Api").ToDictionary(s => s.Key, s => s.Value),
            Prompts = all.Where(s => s.Category == "Prompt").ToDictionary(s => s.Key, s => s.Value),
            Ffmpeg = all.Where(s => s.Category == "FFmpeg").ToDictionary(s => s.Key, s => s.Value),
            Voice = all.Where(s => s.Category == "Voice").ToDictionary(s => s.Key, s => s.Value),
        };
    }

    public async Task<ProductionRulesDto> GetProductionRulesAsync(CancellationToken ct = default)
    {
        var all = await GetAllAsDictionaryAsync(ct);
        return ProductionRulesDto.FromSettings(all);
    }

    public async Task SaveBulkAsync(IEnumerable<(string Key, string Value)> settings, CancellationToken ct = default)
    {
        var updates = settings.ToList();
        if (updates.Count == 0)
            return;

        var keys = updates.Select(u => u.Key).ToList();
        var existing = await db.SystemSettings
            .Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, ct);

        foreach (var (key, value) in updates)
        {
            if (key.Contains("Telegram", StringComparison.OrdinalIgnoreCase))
                continue;
            if (existing.TryGetValue(key, out var setting))
            {
                setting.Value = value;
                setting.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                var added = new Core.Entities.SystemSetting
                {
                    Key = key,
                    Value = value,
                    Category = InferCategory(key),
                    IsSecret = IsLikelySecretKey(key),
                    UpdatedAt = DateTime.UtcNow
                };
                db.SystemSettings.Add(added);
                existing[key] = added;
            }
        }

        await db.SaveChangesAsync(ct);
        foreach (var key in keys)
            InvalidateCache(key);
    }

    private bool TryGetCached(string key, out string? value)
    {
        if (_cache.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTime.UtcNow)
        {
            value = entry.Value;
            return true;
        }

        value = null;
        return false;
    }

    private void SetCached(string key, string? value) =>
        _cache[key] = (value, DateTime.UtcNow.Add(CacheTtl));

    private void InvalidateCache(string key) => _cache.TryRemove(key, out _);

    private static bool IsLikelySecretKey(string key) =>
        key is SettingKeys.OpenAiApiKey or SettingKeys.GeminiApiKey or SettingKeys.ElevenLabsApiKey
            or SettingKeys.HuggingFaceApiKey;

    private static string InferCategory(string key) => key switch
    {
        _ when key.StartsWith("Api.") => "Api",
        _ when key.StartsWith("Local.") => "Local",
        _ when key.StartsWith("Prompt.") => "Prompt",
        _ when key.StartsWith("Voice.") => "Voice",
        _ => "FFmpeg"
    };
}
