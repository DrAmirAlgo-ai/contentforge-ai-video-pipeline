using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;

namespace ContentEmpireSystem.Infrastructure.Services.Local;

/// <summary>Resolves localhost URLs for LM Studio and ComfyUI.</summary>
public sealed class LocalEndpointResolver(ISystemSettingsService settings)
{
    public async Task<string> ResolveComfyUiBaseUrlAsync(CancellationToken ct = default) =>
        (await settings.GetOrDefaultAsync(SettingKeys.LocalComfyUiBaseUrl, "http://127.0.0.1:8188", ct))
        .Trim().TrimEnd('/');

    public async Task<string> ResolveLmStudioBaseUrlAsync(CancellationToken ct = default) =>
        (await settings.GetOrDefaultAsync(SettingKeys.LmStudioBaseUrl, "http://127.0.0.1:1234", ct))
        .Trim().TrimEnd('/');
}
