namespace ContentEmpireSystem.Core;

public static class VideoProviderNames
{
    public const string WanTi2v = "WanTI2V";
    public const string WanT2v = "WanT2V";
    public const string ComfyUi = "ComfyUI";

    public static string? Normalize(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return null;

        return provider.Trim().ToLowerInvariant() switch
        {
            "wanti2v" or "wan_ti2v" or "wan-i2v" or "wan i2v" or "ti2v" => WanTi2v,
            "want2v" or "wan_t2v" or "wan-t2v" or "wan t2v" or "t2v" or "text-to-video" => WanT2v,
            "comfyui" or "comfy" => ComfyUi,
            _ => provider.Trim()
        };
    }

    public static bool IsLocalComfy(string provider) =>
        provider is WanTi2v or WanT2v or ComfyUi;

    public static bool IsSupported(string? provider) =>
        provider is WanTi2v or WanT2v or ComfyUi;
}
