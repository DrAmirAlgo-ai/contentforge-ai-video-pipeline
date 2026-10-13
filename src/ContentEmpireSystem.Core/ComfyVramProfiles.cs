namespace ContentEmpireSystem.Core;

/// <summary>Safe ComfyUI generation caps for laptop GPUs (8–11 GB).</summary>
public static class ComfyVramProfiles
{
    /// <summary>RTX 5060 laptop reports ~8144 MiB; treat ≤11 GB as low-VRAM.</summary>
    public const int LowVramTotalThresholdMiB = 11264;

    public const int WanMinFreeBeforeStartMiB = 5800;
    public const int FluxMinFreeBeforeStartMiB = 4200;
    public const int FluxInterImageSettleMsDefault = 2000;
    public const int EngineSwitchSettleSecDefault = 6;
    public const int ComfyRestartTimeoutSecDefault = 120;
    public const int FluxRestartGraceMinutes = 5;

    /// <summary>8GB laptop: no DynamicVRAM/pinned; novram keeps CLIP/T5 on CPU to avoid GGUF unpatch crash.</summary>
    public const string ComfyStabilityLaunchArgs = "--disable-dynamic-vram --disable-pinned-memory --novram --reserve-vram 2";

    public static bool IsLowVramGpu(int? totalVramMiB) =>
        totalVramMiB is > 0 and <= LowVramTotalThresholdMiB;

    public static bool ShouldUseLowVramWanWorkflow(int? totalVramMiB, bool settingEnabled) =>
        settingEnabled || IsLowVramGpu(totalVramMiB);
}
