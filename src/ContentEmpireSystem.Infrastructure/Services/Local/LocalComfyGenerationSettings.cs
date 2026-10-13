using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.Enums;

using ContentEmpireSystem.Core.Interfaces;



namespace ContentEmpireSystem.Infrastructure.Services.Local;



public sealed record LocalComfyGenerationSettings(

    int FluxSteps,

    int FluxWidth,

    int FluxHeight,

    int WanSteps,

    double WanCfg,

    double WanDenoise,

    int WanWidth,

    int WanHeight,

    int WanFrameCount,

    string WanUnetName,

    string WanVaeName,

    string WanNegativePrompt)

{

    public const string DefaultWanUnetName = "Wan2.2-TI2V-5B-Q4_K_S.gguf";

    public const string DefaultWanVaeName = "wan2.2_vae.safetensors";

    public const string DefaultWanNegativePrompt =

        "blurry, low quality, static, distorted, face morphing, deformed face, identity change, "

        + "flickering, jitter, extra limbs, duplicate face, warped features, unnatural motion, "
        + "ghosting, smearing, temporal blur, hazy overlay, foggy layer, double exposure";



    public static async Task<LocalComfyGenerationSettings> LoadAsync(

        ISystemSettingsService settings, CancellationToken ct = default)

    {

        var all = await settings.GetAllAsDictionaryAsync(ct);

        static string V(IReadOnlyDictionary<string, string> d, string key, string fallback) =>

            d.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;



        var fps = ParseInt(V(all, SettingKeys.OutputVideoFps, "23"), 23, 15, 60);

        var sceneSec = ParseDouble(V(all, SettingKeys.ImageDurationSec, "5"), 5, 1, 30);

        var frameCount = Math.Clamp((int)Math.Round(fps * sceneSec), 49, 145);

        var fluxPreset = FluxQualityPresets.Normalize(
            V(all, SettingKeys.LocalFluxQualityPreset, FluxQualityPresets.Default));
        var manualFluxW = ParseInt(V(all, SettingKeys.LocalFluxWidth, "640"), 640, 384, 1024);
        var manualFluxH = ParseInt(V(all, SettingKeys.LocalFluxHeight, "1152"), 1152, 640, 1536);
        var manualFluxSteps = ParseInt(V(all, SettingKeys.LocalFluxSteps, "8"), 8, 4, 20);
        var fluxResolved = FluxQualityPresets.Resolve(fluxPreset, manualFluxW, manualFluxH, manualFluxSteps);

        return new LocalComfyGenerationSettings(

            FluxSteps: fluxResolved.Steps,

            FluxWidth: fluxResolved.Width,

            FluxHeight: fluxResolved.Height,

            WanSteps: ParseInt(V(all, SettingKeys.LocalWanSteps, "28"), 28, 12, 40),

            WanCfg: ParseDouble(V(all, SettingKeys.LocalWanCfg, "4.0"), 4.0, 1, 8),

            WanDenoise: ParseDouble(V(all, SettingKeys.LocalWanDenoise, "0.6"), 0.6, 0.3, 1.0),

            WanWidth: ParseInt(V(all, SettingKeys.LocalWanWidth, "480"), 480, 384, 720),

            WanHeight: ParseInt(V(all, SettingKeys.LocalWanHeight, "832"), 832, 640, 1280),

            WanFrameCount: frameCount,

            WanUnetName: V(all, SettingKeys.LocalWanUnetName, DefaultWanUnetName),

            WanVaeName: V(all, SettingKeys.LocalWanVaeName, DefaultWanVaeName),

            WanNegativePrompt: V(all, SettingKeys.WanMotionNegativePrompt,
                V(all, SettingKeys.LocalWanNegativePrompt, DefaultWanNegativePrompt)));

    }

    /// <summary>Clamp generation settings on 8–11 GB GPUs to prevent Comfy OOM crashes.</summary>
    public static LocalComfyGenerationSettings ApplyVramProfile(
        LocalComfyGenerationSettings settings, int? totalVramMiB)
    {
        if (!ComfyVramProfiles.IsLowVramGpu(totalVramMiB))
            return settings;

        var cap = LowVramFluxCap;
        return settings with
        {
            FluxWidth = Math.Min(settings.FluxWidth, cap.Width),
            FluxHeight = Math.Min(settings.FluxHeight, cap.Height),
            FluxSteps = Math.Min(settings.FluxSteps, cap.Steps),
            WanSteps = Math.Min(settings.WanSteps, 20),
            WanFrameCount = Math.Min(settings.WanFrameCount, 69),
            WanWidth = Math.Min(settings.WanWidth, 480),
            WanHeight = Math.Min(settings.WanHeight, 832),
        };
    }

    private static FluxQualityPresets.Values LowVramFluxCap =>
        FluxQualityPresets.Resolve(FluxQualityPresets.Balanced, null, null, null);



    private static int ParseInt(string raw, int fallback, int min, int max) =>

        int.TryParse(raw, out var value) ? Math.Clamp(value, min, max) : fallback;



    private static double ParseDouble(string raw, double fallback, double min, double max) =>

        double.TryParse(raw, System.Globalization.NumberStyles.Float,

            System.Globalization.CultureInfo.InvariantCulture, out var value)

            ? Math.Clamp(value, min, max)

            : fallback;

}


