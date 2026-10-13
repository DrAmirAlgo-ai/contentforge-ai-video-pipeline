using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Enums;

namespace ContentEmpireSystem.Core.Interfaces;

public interface ISystemSettingsService
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task<string> GetOrDefaultAsync(string key, string defaultValue, CancellationToken ct = default);
    Task<bool> HasValueAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task<Dictionary<string, string>> GetCategoryAsync(string category, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetAllAsDictionaryAsync(CancellationToken ct = default);
    Task SaveBulkAsync(IEnumerable<(string Key, string Value)> settings, CancellationToken ct = default);
    Task<List<SettingItemDto>> GetAllForAdminAsync(CancellationToken ct = default);
    Task<SettingsGroupDto> GetGroupedAsync(CancellationToken ct = default);
    Task<ProductionRulesDto> GetProductionRulesAsync(CancellationToken ct = default);
}
