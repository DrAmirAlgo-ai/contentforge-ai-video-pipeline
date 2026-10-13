using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Services.Local;

namespace ContentEmpireSystem.Infrastructure.Services;

/// <summary>Dashboard DB wins over .env for deployment profile at runtime.</summary>
internal static class DeploymentProfileSource
{
    public static async Task<string> ResolveAsync(ISystemSettingsService settings, CancellationToken ct = default)
    {
        var fromDb = (await settings.GetOrDefaultAsync(SettingKeys.DeploymentProfile, "", ct)).Trim();
        if (!string.IsNullOrEmpty(fromDb))
            return DeploymentProfileResolver.Resolve(fromDb);

        return DeploymentProfileResolver.Resolve(AppEnvironmentVariables.GetDeploymentProfile());
    }
}
