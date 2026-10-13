using System.Globalization;

using ContentEmpireSystem.Core;

using ContentEmpireSystem.Core.Enums;

using ContentEmpireSystem.Core.Interfaces;



namespace ContentEmpireSystem.Infrastructure.Services;



public sealed class MontageSettings

{

    public string FfmpegPath { get; init; } = "ffmpeg";

    public string OutputDirectory { get; init; } = "output";

    public bool ForceSyncDuration { get; init; } = true;

    public string SyncMode { get; init; } = "audio_master";

    public string MediaShortfallAction { get; init; } = "stretch";

    public string MediaOverflowAction { get; init; } = "trim";

    public string ImageTimingMode { get; init; } = "equal_split";

    public string SubtitleSyncMode { get; init; } = "scale_to_audio";

    public double SubtitleOffsetMs { get; init; }

    public int OutputFps { get; init; } = 23;

    public int Width { get; init; } = 480;

    public int Height { get; init; } = 832;

    public double KenBurnsSpeed { get; init; } = 0.0003;

    public string KenBurnsDirection { get; init; } = "zoom_in";

    public string ImageMotionAssignment { get; init; } = "auto";

    public string TransitionType { get; init; } = "fade";

    public double TransitionDurationSec { get; init; } = 0.5;

    public double ImageDurationSec { get; init; } = 5;

    public double WanMaxStretchFactor { get; init; } = 1.12;

    public string FontSize { get; init; } = "48";

    public string FontName { get; init; } = "Arial";

    public string PrimaryColor { get; init; } = "&H00FFFFFF";

    public string HighlightColor { get; init; } = "&H0000FFFF";

    public string BackColor { get; init; } = "&H80000000";

    public int SubtitleAlignment { get; init; } = 2;

    public int SubtitleOutline { get; init; } = 3;

    public bool SubtitlesEnabled { get; init; } = true;

    public double VoiceVolume { get; init; } = 1.0;

    public double MusicVolume { get; init; } = 1.0;

    public double AudioDuckingLevel { get; init; } = 0.25;

    public bool BackgroundMusicEnabled { get; init; } = true;

    public string? BackgroundMusicPath { get; init; }

    public int VideoCrf { get; init; } = 23;

    public bool QuickPreviewMode { get; init; }



    public static async Task<MontageSettings> LoadAsync(ISystemSettingsService settings, CancellationToken ct)

    {

        var all = await settings.GetAllAsDictionaryAsync(ct);

        var aspect = V(all, SettingKeys.VideoAspectRatio, "9:16");

        var width = ParseInt(V(all, SettingKeys.VideoWidth, "0"), 0);

        var height = ParseInt(V(all, SettingKeys.VideoHeight, "0"), 0);



        if (width <= 0 || height <= 0)

        {

            if (string.Equals(aspect, "custom", StringComparison.OrdinalIgnoreCase))

                (width, height) = (480, 832);

            else

                (width, height) = ResolveAspectRatio(aspect);

        }



        var kenBurnsRaw = ParseDouble(V(all, SettingKeys.KenBurnsSpeed, "0.0008"), 0.0008);



        return new MontageSettings

        {

            FfmpegPath = V(all, SettingKeys.FfmpegPath, "ffmpeg"),

            OutputDirectory = V(all, SettingKeys.OutputDirectory, "output"),

            ForceSyncDuration = MediaDurationHelper.ParseForceSync(V(all, SettingKeys.ForceSyncDuration, "true")),

            SyncMode = ResolveSyncModeFromDictionary(all),

            MediaShortfallAction = NormalizeMediaShortfallAction(V(all, SettingKeys.MediaShortfallAction, "freeze_last")),

            MediaOverflowAction = NormalizeMediaOverflowAction(V(all, SettingKeys.MediaOverflowAction, "trim")),

            ImageTimingMode = NormalizeImageTimingMode(V(all, SettingKeys.ImageTimingMode, "equal_split")),

            SubtitleSyncMode = NormalizeSubtitleSyncMode(V(all, SettingKeys.SubtitleSyncMode, "scale_to_audio")),

            SubtitleOffsetMs = ParseDouble(V(all, SettingKeys.SubtitleOffsetMs, "0"), 0),

            OutputFps = Math.Max(1, ParseInt(V(all, SettingKeys.OutputVideoFps, "23"), 23)),

            Width = width,

            Height = height,

            KenBurnsSpeed = Math.Max(kenBurnsRaw, MinVisibleKenBurnsSpeed),

            KenBurnsDirection = V(all, SettingKeys.KenBurnsDirection, "zoom_in"),

            ImageMotionAssignment = NormalizeImageMotionAssignment(V(all, SettingKeys.ImageMotionAssignment, "auto")),

            TransitionType = V(all, SettingKeys.TransitionType, "fade"),

            TransitionDurationSec = ParseDouble(V(all, SettingKeys.TransitionDurationSec, "0.5"), 0.5),

            ImageDurationSec = ParseDouble(V(all, SettingKeys.ImageDurationSec, "5"), 5),

            WanMaxStretchFactor = Math.Clamp(
                ParseDouble(V(all, SettingKeys.WanMaxStretchFactor, "1.12"), 1.12),
                1.0,
                2.0),

            FontSize = V(all, SettingKeys.CaptionFontSize, "48"),

            FontName = V(all, SettingKeys.SubtitleFontName, "Arial"),

            PrimaryColor = AssColorHelper.Normalize(V(all, SettingKeys.SubtitlePrimaryColor, "&H00FFFFFF"), "&H00FFFFFF"),

            HighlightColor = AssColorHelper.Normalize(V(all, SettingKeys.SubtitleHighlightColor, "&H0000FFFF"), "&H0000FFFF"),

            BackColor = AssColorHelper.Normalize(V(all, SettingKeys.SubtitleBackColor, "&H80000000"), "&H80000000"),

            SubtitleAlignment = ParseInt(V(all, SettingKeys.SubtitleAlignment, "2"), 2),

            SubtitleOutline = ParseInt(V(all, SettingKeys.SubtitleOutline, "3"), 3),

            SubtitlesEnabled = MediaDurationHelper.ParseForceSync(V(all, SettingKeys.SubtitlesEnabled, "true")),

            VoiceVolume = ParseDouble(V(all, SettingKeys.VoiceVolume, "1.0"), 1.0),

            MusicVolume = ParseDouble(V(all, SettingKeys.MusicVolume, "1.0"), 1.0),

            AudioDuckingLevel = ParseDouble(V(all, SettingKeys.AudioDuckingLevel, "0.25"), 0.25),

            BackgroundMusicEnabled = MediaDurationHelper.ParseForceSync(V(all, SettingKeys.BackgroundMusicEnabled, "true")),

            BackgroundMusicPath = all.TryGetValue(SettingKeys.BackgroundMusicPath, out var bgm) ? bgm : null,

            VideoCrf = Math.Clamp(ParseInt(V(all, SettingKeys.VideoCrf, "23"), 23), 18, 28),

            QuickPreviewMode = MediaDurationHelper.ParseForceSync(V(all, SettingKeys.QuickPreviewMode, "false"))

        };

    }



    public static (int Width, int Height) ResolveAspectRatio(string aspect) => aspect.Trim().ToLowerInvariant() switch

    {

        "16:9" => (832, 480),

        "1:1" => (480, 480),

        "custom" => (480, 832),

        _ => (480, 832)

    };



    /// <summary>Minimum zoom/pan speed so motion stays visible (values below ~0.0005 look static on long clips).</summary>

    private const double MinVisibleKenBurnsSpeed = 0.0008;



    /// <summary>Upscale before zoompan so pan/zoom have pixel headroom (always ≥ output 1080×1920).</summary>

    public string BuildPreZoomScaleFilter(double maxZoomHeadroom = ImageMotionDefaults.MaxZoom)

    {

        var scaleFactor = Math.Max(1.3, maxZoomHeadroom);

        var upW = (int)Math.Ceiling(Width * scaleFactor / 2.0) * 2;

        var upH = (int)Math.Ceiling(Height * scaleFactor / 2.0) * 2;

        return $"scale={upW}:{upH}:force_original_aspect_ratio=increase,crop={upW}:{upH},setsar=1";

    }



    public string BuildImageKenBurnsChain(

        int frames,

        string? directionOverride = null,

        double? motionSpeedOverride = null,

        double? maxZoomOverride = null)

    {

        var dir = ImageMotionHelper.NormalizeMode(directionOverride ?? KenBurnsDirection);

        var maxZoom = ImageMotionHelper.NormalizeMaxZoom(maxZoomOverride);

        var scaleHeadroom = ImageMotionHelper.IsOversizePanMode(dir)

            ? Math.Max(maxZoom, ImageMotionDefaults.OversizePanMinZoom)

            : maxZoom;



        return

            $"{BuildPreZoomScaleFilter(scaleHeadroom)}," +

            $"{BuildZoomPanFilter(frames, directionOverride, motionSpeedOverride, maxZoomOverride)},setpts=PTS-STARTPTS";

    }



    public string BuildZoomPanFilter(

        int frames,

        string? directionOverride = null,

        double? motionSpeedOverride = null,

        double? maxZoomOverride = null)

    {

        var dir = ImageMotionHelper.NormalizeMode(directionOverride ?? KenBurnsDirection);



        if (dir == "static")

            return $"fps={OutputFps}";



        var frameCount = Math.Max(frames, 1);

        var motionSpeed = ImageMotionHelper.NormalizeMotionSpeed(motionSpeedOverride);

        var maxZoom = ImageMotionHelper.NormalizeMaxZoom(maxZoomOverride);

        var speed = FormatExpr(motionSpeed);

        var maxZ = FormatExpr(maxZoom);

        var speedScale = motionSpeed / ImageMotionDefaults.MotionSpeed;

        var panZoom = FormatExpr(Math.Max(1.02, Math.Min(maxZoom * 0.88, maxZoom - 0.04)));

        var panDistance = Width * ImageMotionDefaults.PanTravelRatio * speedScale;

        var panStepX = FormatExpr(panDistance / frameCount);

        var panDistanceY = Height * ImageMotionDefaults.PanTravelRatio * speedScale;

        var panStepY = FormatExpr(panDistanceY / frameCount);

        var zoomOutStep = FormatExpr((maxZoom - 1.0) * speedScale / frameCount);

        var halfW = FormatExpr(Width / 2.0);

        var halfH = FormatExpr(Height / 2.0);

        var pulseMid = FormatExpr(frameCount / 2.0);



        var (zExpr, xExpr, yExpr) = ImageMotionHelper.IsOversizePanMode(dir)

            ? BuildOversizePanExprs(dir, frameCount, maxZoom, halfW, halfH)

            : dir switch

        {

            "zoom_out" => ($"max({maxZ}-on*{zoomOutStep},1.0)", "iw/2-(iw/zoom/2)", "ih/2-(ih/zoom/2)"),

            "pan_right" => (panZoom, $"min(on*{panStepX},iw-{Width})", halfH),

            "pan_left" => (panZoom, $"max(iw-{Width}-on*{panStepX},0)", halfH),

            "pan_up" => (panZoom, halfW, $"max(ih-{Height}-on*{panStepY},0)"),

            "pan_down" => (panZoom, halfW, $"min(on*{panStepY},ih-{Height})"),

            "ken_burns" => ($"min(1+on*{speed},{maxZ})", $"min(on*{panStepX},iw-iw/zoom)", "ih/2-(ih/zoom/2)"),

            "ken_burns_out" => ($"max({maxZ}-on*{zoomOutStep},1.0)", $"max(iw-iw/zoom-on*{panStepX},0)", "ih/2-(ih/zoom/2)"),

            "zoom_pulse" => ($"if(lt(on,{pulseMid}),1+on*{speed}*2,{maxZ}-(on-{pulseMid})*{speed}*2)", halfW, halfH),

            "diagonal_dr" => ($"min(1+on*{speed},{maxZ})", $"min(on*{panStepX},iw-iw/zoom)", $"min(on*{panStepY},ih-ih/zoom)"),

            "diagonal_dl" => ($"min(1+on*{speed},{maxZ})", $"max(iw-iw/zoom-on*{panStepX},0)", $"min(on*{panStepY},ih-ih/zoom)"),

            "random" => BuildRandomKenBurns(frameCount, maxZoom, Width, Height, speed, panStepX, panStepY, maxZ, panZoom, zoomOutStep, halfW, halfH),

            _ => ($"min(1+on*{speed},{maxZ})", "iw/2-(iw/zoom/2)", "ih/2-(ih/zoom/2)")

        };



        return $"zoompan=z='{zExpr}':d={frameCount}:x='{xExpr}':y='{yExpr}':s={Width}x{Height}:fps={OutputFps}";

    }



    /// <summary>

    /// Oversized image pan: scale beyond the frame, then sweep across the full hidden area (cinematic pan).

    /// </summary>

    private static (string Z, string X, string Y) BuildOversizePanExprs(

        string dir,

        int frameCount,

        double maxZoom,

        string halfW,

        string halfH)

    {

        var oversizeZoom = FormatExpr(Math.Max(ImageMotionDefaults.OversizePanMinZoom, maxZoom));

        var panDenom = FormatExpr(Math.Max(frameCount - 1, 1));

        var maxPanX = $"iw-iw/zoom";

        var maxPanY = $"ih-ih/zoom";

        var panRightX = $"min(on/{panDenom}*({maxPanX}),{maxPanX})";

        var panLeftX = $"max({maxPanX}-on/{panDenom}*({maxPanX}),0)";

        var panDownY = $"min(on/{panDenom}*({maxPanY}),{maxPanY})";

        var panUpY = $"max({maxPanY}-on/{panDenom}*({maxPanY}),0)";



        return dir switch

        {

            "oversize_pan_right" => (oversizeZoom, panRightX, halfH),

            "oversize_pan_left" => (oversizeZoom, panLeftX, halfH),

            "oversize_pan_up" => (oversizeZoom, halfW, panUpY),

            _ => (oversizeZoom, halfW, panDownY)

        };

    }



    private static string FormatExpr(double value) =>

        value.ToString("0.0########", CultureInfo.InvariantCulture);



    /// <summary>FFmpeg output flags for MP4 that plays in Telegram, Messenger, and mobile gallery without buffering hangs.</summary>

    public string BuildMobileSafeOutputArgs(string preset, int crf)

    {

        var fps = OutputFps.ToString(CultureInfo.InvariantCulture);

        var gop = Math.Max(OutputFps * 2, 25).ToString(CultureInfo.InvariantCulture);

        var crfStr = crf.ToString(CultureInfo.InvariantCulture);

        return

            " -f mp4 -movflags +faststart" +

            " -c:v libx264 -pix_fmt yuv420p -profile:v main -level:v 4.1" +

            $" -preset {preset} -crf {crfStr}" +

            $" -r {fps} -g {gop} -keyint_min {fps}" +

            " -tag:v avc1" +

            " -c:a aac -profile:a aac_low -b:a 128k -ar 44100 -ac 2" +

            " -tag:a mp4a";

    }



    public string? ResolveXfadeTransition() => TransitionType.ToLowerInvariant() switch

    {

        "fade" => "fade",

        "slide" => "slideleft",

        _ => null

    };



    private static (string Z, string X, string Y) BuildRandomKenBurns(
        int frameCount, double maxZoom,
        int width, int height,
        string speed, string panStepX, string panStepY, string maxZ, string panZoom,
        string zoomOutStep, string halfW, string halfH)

    {

        var pick = Random.Shared.Next(10);

        var oversizeDirs = new[] { "oversize_pan_right", "oversize_pan_left", "oversize_pan_up", "oversize_pan_down" };

        return pick switch

        {

            0 => ($"min(1+on*{speed},{maxZ})", "iw/2-(iw/zoom/2)", "ih/2-(ih/zoom/2)"),

            1 => ($"max({maxZ}-on*{zoomOutStep},1.0)", "iw/2-(iw/zoom/2)", "ih/2-(ih/zoom/2)"),

            2 => (panZoom, $"min(on*{panStepX},iw-{width})", halfH),

            3 => (panZoom, $"max(iw-{width}-on*{panStepX},0)", halfH),

            4 => (panZoom, halfW, $"min(on*{panStepY},ih-{height})"),

            >= 5 and <= 8 => BuildOversizePanExprs(oversizeDirs[pick - 5], frameCount, maxZoom, halfW, halfH),

            _ => ($"min(1+on*{speed},{maxZ})", $"min(on*{panStepX},iw-iw/zoom)", $"min(on*{panStepY},ih-ih/zoom)")

        };

    }



    private static int ParseInt(string value, int fallback) =>

        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;



    private static double ParseDouble(string value, double fallback) =>

        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : fallback;



    private static string ResolveSyncModeFromDictionary(IReadOnlyDictionary<string, string> all)

    {

        var configured = V(all, SettingKeys.SyncMode, "");

        if (!string.IsNullOrWhiteSpace(configured))

            return NormalizeSyncMode(configured);



        var legacyForceSync = MediaDurationHelper.ParseForceSync(V(all, SettingKeys.ForceSyncDuration, "true"));

        return legacyForceSync ? "audio_master" : "shortest";

    }



    private static string V(IReadOnlyDictionary<string, string> all, string key, string fallback) =>

        all.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;



    private static string NormalizeSyncMode(string value) => value.Trim().ToLowerInvariant() switch

    {

        "media_master" => "media_master",

        "shortest" => "shortest",

        _ => "audio_master"

    };



    private static string NormalizeMediaShortfallAction(string value) => value.Trim().ToLowerInvariant() switch

    {

        "loop_sequence" => "loop_sequence",

        "loop_last" => "loop_last",

        "freeze_last" => "freeze_last",

        _ => "stretch"

    };



    private static string NormalizeMediaOverflowAction(string value) => value.Trim().ToLowerInvariant() switch

    {

        "keep" => "keep",

        _ => "trim"

    };



    private static string NormalizeImageTimingMode(string value) => value.Trim().ToLowerInvariant() switch

    {

        "fixed_duration" => "fixed_duration",

        _ => "equal_split"

    };



    private static string NormalizeImageMotionAssignment(string value) => value.Trim().ToLowerInvariant() switch

    {

        "manual" => "manual",

        _ => "auto"

    };



    private static string NormalizeSubtitleSyncMode(string value) => value.Trim().ToLowerInvariant() switch

    {

        "native" => "native",

        "scale_to_video" => "scale_to_video",

        _ => "scale_to_audio"

    };

}


