namespace ContentEmpireSystem.Core;

/// <summary>Dashboard deployment presets — local GPU stack vs cloud APIs only.</summary>
public static class DeploymentProfileNames
{
    public const string Local = "Local";
    public const string Online = "Online";

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim().ToLowerInvariant() switch
        {
            "local" or "localhost" or "thispc" => Local,
            "online" or "cloud" or "api" => Online,
            // Legacy remote / RunPod profiles map to Local (removed).
            "runpod" or "run_pod" or "cloud-gpu" or "remote" or "remotegpu" or "remote_gpu"
                or "vpn" or "tunnel" => Local,
            _ when value.Equals(Local, StringComparison.OrdinalIgnoreCase) => Local,
            _ when value.Equals(Online, StringComparison.OrdinalIgnoreCase) => Online,
            _ when value.Equals("RunPod", StringComparison.OrdinalIgnoreCase) => Local,
            _ when value.Equals("RemoteGpu", StringComparison.OrdinalIgnoreCase) => Local,
            _ => null
        };
    }
}
