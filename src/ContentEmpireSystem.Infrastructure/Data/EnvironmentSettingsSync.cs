using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Infrastructure.Services.Local;
using Microsoft.EntityFrameworkCore;

namespace ContentEmpireSystem.Infrastructure.Data;

/// <summary>Seeds empty DB settings from .env — never overrides user-chosen deployment profile.</summary>
internal static class EnvironmentSettingsSync
{
    private static readonly string[] LegacyRemoteProfileValues =
        ["RunPod", "RemoteGpu", "runpod", "remotegpu", "remote", "Remote"];

    private static readonly (string OldKey, string NewKey)[] LegacySettingKeyRenames =
    [
        ("Local.ComfyUI.RemoteOutputDir", SettingKeys.LocalComfyOutputPrefix),
    ];

    public static async Task EnsureWorkflowDefaultsAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        var keys = WorkflowDefaults.All.Keys.ToList();
        var existing = await db.SystemSettings
            .Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, ct);

        foreach (var (key, defaultPath) in WorkflowDefaults.All)
        {
            if (existing.TryGetValue(key, out var setting))
            {
                if (!string.IsNullOrWhiteSpace(setting.Value))
                    continue;
                setting.Value = defaultPath;
            }
            else
            {
                db.SystemSettings.Add(new Core.Entities.SystemSetting
                {
                    Key = key,
                    Value = defaultPath,
                    Category = "Local",
                    Description = "ComfyUI workflow JSON (auto-default)",
                    IsSecret = false
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public static async Task MigrateLegacyDeploymentSettingsAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        var changed = false;

        var profile = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == SettingKeys.DeploymentProfile, ct);
        if (profile is not null
            && LegacyRemoteProfileValues.Any(v =>
                string.Equals(profile.Value, v, StringComparison.OrdinalIgnoreCase)))
        {
            profile.Value = DeploymentProfileNames.Local;
            changed = true;
        }

        var workflowKeys = new[]
        {
            SettingKeys.LocalComfyWorkflowFlux,
            SettingKeys.LocalComfyWorkflowWanTi2v,
            SettingKeys.LocalComfyWorkflowWanT2v,
        };
        foreach (var key in workflowKeys)
        {
            var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
            if (setting is null || string.IsNullOrWhiteSpace(setting.Value))
                continue;
            var updated = setting.Value.Replace("deploy/runpod/workflows", "deploy/workflows", StringComparison.Ordinal);
            if (updated == setting.Value)
                continue;
            setting.Value = updated;
            changed = true;
        }

        foreach (var (oldKey, newKey) in LegacySettingKeyRenames)
        {
            var legacy = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == oldKey, ct);
            if (legacy is null || string.IsNullOrWhiteSpace(legacy.Value))
                continue;

            var target = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == newKey, ct);
            if (target is null)
            {
                db.SystemSettings.Add(new Core.Entities.SystemSetting
                {
                    Key = newKey,
                    Value = legacy.Value,
                    Category = "Local",
                    Description = "ComfyUI output subfolder prefix (migrated)",
                    IsSecret = false
                });
            }
            else if (string.IsNullOrWhiteSpace(target.Value))
            {
                target.Value = legacy.Value;
            }

            db.SystemSettings.Remove(legacy);
            changed = true;
        }

        var obsoleteKeys = new[]
        {
            "Local.EndpointMode",
            "Local.ComfyUI.RemoteBaseUrl",
            "Local.LMStudio.RemoteBaseUrl",
            "Local.Remote.ApiKey",
            "Local.ComfyUI.RemoteInputDir",
            "Local.ComfyUI.RemoteOutputDir",
        };
        var obsolete = await db.SystemSettings.Where(s => obsoleteKeys.Contains(s.Key)).ToListAsync(ct);
        if (obsolete.Count > 0)
        {
            db.SystemSettings.RemoveRange(obsolete);
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync(ct);
    }

    /// <summary>Local production defaults: Wan required, 23fps output, 5s scene timing.</summary>
    public static async Task EnsureLocalVideoProductionDefaultsAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        var updates = new Dictionary<string, string>
        {
            [SettingKeys.RequireWanAnimation] = "true",
            [SettingKeys.ScriptProvider] = "Auto",
            [SettingKeys.OutputVideoFps] = "23",
            [SettingKeys.ImageDurationSec] = "5",
            [SettingKeys.MediaShortfallAction] = "freeze_last",
            [SettingKeys.WanMaxStretchFactor] = "1.12",
            [SettingKeys.LocalFluxQualityPreset] = FluxQualityPresets.Default,
            [SettingKeys.LocalComfySafeVramFree] = "true",
            [SettingKeys.LocalComfyInterImageDelayMs] = "2000",
            [SettingKeys.LocalComfyEngineSwitchDelaySec] = "6",
            [SettingKeys.LocalComfyAutoRestartAfterFlux] = "true",
            [SettingKeys.LocalComfyInstallRoot] = @"D:\ComfyUI",
            [SettingKeys.LocalComfyRestartTimeoutSec] = "120",
            [SettingKeys.LocalFluxSteps] = "8",
            [SettingKeys.LocalFluxWidth] = "640",
            [SettingKeys.LocalFluxHeight] = "1152",
            [SettingKeys.LocalWanSteps] = "20",
            [SettingKeys.LocalWanTiledVaeDecode] = "true",
            [SettingKeys.LocalWanCfg] = "4.0",
            [SettingKeys.LocalWanDenoise] = "0.6",
            [SettingKeys.VideoWidth] = "480",
            [SettingKeys.VideoHeight] = "832",
        };

        var keys = updates.Keys.ToList();
        var settings = await db.SystemSettings.Where(s => keys.Contains(s.Key)).ToListAsync(ct);
        var changed = false;

        foreach (var (key, value) in updates)
        {
            var setting = settings.FirstOrDefault(s => s.Key == key);
            if (setting is null)
            {
                db.SystemSettings.Add(new Core.Entities.SystemSetting
                {
                    Key = key,
                    Value = value,
                    Category = key.StartsWith("Api.", StringComparison.Ordinal) ? "Api" : "FFmpeg",
                    Description = "Local Wan video default",
                    IsSecret = false
                });
                changed = true;
                continue;
            }

            if (key == SettingKeys.RequireWanAnimation && !string.Equals(setting.Value, "true", StringComparison.OrdinalIgnoreCase))
            {
                setting.Value = value;
                changed = true;
            }
            else if (key == SettingKeys.OutputVideoFps && setting.Value is "25" or "24")
            {
                setting.Value = value;
                changed = true;
            }
            else if (key == SettingKeys.MediaShortfallAction && setting.Value is "stretch")
            {
                setting.Value = value;
                changed = true;
            }
            else if (key is SettingKeys.LocalFluxSteps or SettingKeys.LocalFluxWidth or SettingKeys.LocalFluxHeight
                     or SettingKeys.LocalWanSteps or SettingKeys.LocalWanCfg
                     or SettingKeys.LocalWanDenoise or SettingKeys.LocalWanTiledVaeDecode
                     or SettingKeys.WanMaxStretchFactor)
            {
                if (string.IsNullOrWhiteSpace(setting.Value)
                    || (key == SettingKeys.LocalFluxSteps && setting.Value is "4" or "12")
                    || (key == SettingKeys.LocalFluxWidth && setting.Value is "480" or "768")
                    || (key == SettingKeys.LocalFluxHeight && setting.Value is "832" or "1344")
                    || (key == SettingKeys.LocalWanSteps && setting.Value is "20" or "24" or "28" or "30")
                    || (key == SettingKeys.LocalWanTiledVaeDecode && setting.Value is "false")
                    || (key == SettingKeys.LocalWanDenoise && IsLegacyWanDenoise(setting.Value))
                    || (key == SettingKeys.LocalWanCfg && setting.Value is "6.5" or "7" or "7.0")
                    || (key == SettingKeys.LocalWanSteps && setting.Value is "28" or "30"))
                {
                    setting.Value = value;
                    changed = true;
                }
            }
            else if (key == SettingKeys.VideoWidth && setting.Value is "1080" or "0" or "")
            {
                setting.Value = value;
                changed = true;
            }
            else if (key == SettingKeys.VideoHeight && setting.Value is "1920" or "0" or "")
            {
                setting.Value = value;
                changed = true;
            }
            else if (key == SettingKeys.ScriptProvider
                     && setting.Value is "Gemini" or "LMStudio" or "Ollama")
            {
                setting.Value = value;
                changed = true;
            }
        }

        if (changed)
            await db.SaveChangesAsync(ct);
    }

    public static async Task ApplyAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        await MigrateLegacyDeploymentSettingsAsync(db, ct);
        await EnsureWorkflowDefaultsAsync(db, ct);
        await EnsureLocalVideoProductionDefaultsAsync(db, ct);

        var candidates = new List<(string Key, string? Value)>
        {
            (SettingKeys.DeploymentProfile, AppEnvironmentVariables.GetDeploymentProfile()),
            (SettingKeys.LmStudioModel, AppEnvironmentVariables.GetLmStudioModel()),
            (SettingKeys.GeminiApiKey, AppEnvironmentVariables.GetGeminiApiKey()),
            (SettingKeys.HuggingFaceApiKey, AppEnvironmentVariables.GetHuggingFaceApiKey()),
        };

        var updates = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.Value))
            .ToList();

        if (updates.Count == 0)
            return;

        var keys = updates.Select(u => u.Key).ToList();
        var existing = await db.SystemSettings
            .Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, ct);

        foreach (var (key, value) in updates)
        {
            var isSecret = key is SettingKeys.GeminiApiKey or SettingKeys.HuggingFaceApiKey
                or SettingKeys.OpenAiApiKey;

            if (existing.TryGetValue(key, out var setting))
            {
                if (!string.IsNullOrWhiteSpace(setting.Value))
                    continue;

                setting.Value = value!;
            }
            else
            {
                db.SystemSettings.Add(new Core.Entities.SystemSetting
                {
                    Key = key,
                    Value = value!,
                    Category = key.StartsWith("Local.", StringComparison.Ordinal) ? "Local" : "Api",
                    Description = "Default from .env (empty DB only)",
                    IsSecret = isSecret
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public static async Task SyncProvidersToDeploymentProfileAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        var profileKey = SettingKeys.DeploymentProfile;
        var profileSetting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == profileKey, ct);
        var profile = DeploymentProfileResolver.Resolve(profileSetting?.Value);

        var keys = new[] { SettingKeys.ScriptProvider, SettingKeys.ImageProvider };
        var settings = await db.SystemSettings.Where(s => keys.Contains(s.Key)).ToListAsync(ct);
        var byKey = settings.ToDictionary(s => s.Key);

        var expectedScript = DeploymentProfileResolver.DefaultScriptProvider(profile);
        if (!byKey.ContainsKey(SettingKeys.ScriptProvider))
        {
            db.SystemSettings.Add(new Core.Entities.SystemSetting
            {
                Key = SettingKeys.ScriptProvider,
                Value = expectedScript,
                Category = "Api",
                Description = "Default from deployment profile",
                IsSecret = false
            });
        }

        var expectedImage = DeploymentProfileResolver.DefaultImageProvider(profile);
        if (!byKey.ContainsKey(SettingKeys.ImageProvider))
        {
            db.SystemSettings.Add(new Core.Entities.SystemSetting
            {
                Key = SettingKeys.ImageProvider,
                Value = expectedImage,
                Category = "Api",
                Description = "Default from deployment profile",
                IsSecret = false
            });
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);
    }

    private static bool IsLegacyWanDenoise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (value is "1" or "1.0")
            return true;

        if (value is "0.6")
            return true;

        return double.TryParse(
            value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed)
            && parsed >= 0.9;
    }
}
