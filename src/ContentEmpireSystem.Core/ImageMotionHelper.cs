using ContentEmpireSystem.Core.Enums;



namespace ContentEmpireSystem.Core;



public static class ImageMotionDefaults

{

    public const double MotionSpeed = 0.002;

    public const double MaxZoom = 1.3;

    public const double OversizePanMinZoom = 1.35;

    public const double PanTravelRatio = 0.42;

}



public static class ImageMotionHelper

{

    private static readonly HashSet<string> AllowedModes = new(StringComparer.OrdinalIgnoreCase)

    {

        "zoom_in", "zoom_out", "pan_left", "pan_right", "pan_up", "pan_down",
        "oversize_pan_right", "oversize_pan_left", "oversize_pan_up", "oversize_pan_down",

        "ken_burns", "ken_burns_out", "zoom_pulse", "diagonal_dr", "diagonal_dl",

        "random", "static"

    };



    public static string NormalizeMode(string? mode)

    {

        if (string.IsNullOrWhiteSpace(mode))

            return "zoom_in";



        var trimmed = mode.Trim();

        if (AllowedModes.Contains(trimmed))

            return trimmed.ToLowerInvariant();



        if (Enum.TryParse<ImageAnimationMode>(trimmed, ignoreCase: true, out var parsed))

            return ToStorageValue(parsed);



        return trimmed.ToLowerInvariant() switch

        {

            "zoomin" => "zoom_in",

            "zoomout" => "zoom_out",

            "panleft" => "pan_left",

            "panright" => "pan_right",

            "panup" => "pan_up",

            "pandown" => "pan_down",

            "oversizepanright" or "oversizedpanright" => "oversize_pan_right",
            "oversizepanleft" or "oversizedpanleft" => "oversize_pan_left",
            "oversizepanup" or "oversizedpanup" => "oversize_pan_up",
            "oversizepandown" or "oversizedpandown" => "oversize_pan_down",

            "kenburns" => "ken_burns",

            "kenburnsout" => "ken_burns_out",

            "zoompulse" => "zoom_pulse",

            "diagonaldr" => "diagonal_dr",

            "diagonaldl" => "diagonal_dl",

            _ => "zoom_in"

        };

    }



    public static string ToStorageValue(ImageAnimationMode mode) => mode switch

    {

        ImageAnimationMode.ZoomOut => "zoom_out",

        ImageAnimationMode.PanLeft => "pan_left",

        ImageAnimationMode.PanRight => "pan_right",

        ImageAnimationMode.PanUp => "pan_up",

        ImageAnimationMode.PanDown => "pan_down",

        ImageAnimationMode.OversizePanRight => "oversize_pan_right",
        ImageAnimationMode.OversizePanLeft => "oversize_pan_left",
        ImageAnimationMode.OversizePanUp => "oversize_pan_up",
        ImageAnimationMode.OversizePanDown => "oversize_pan_down",

        ImageAnimationMode.KenBurns => "ken_burns",

        ImageAnimationMode.KenBurnsOut => "ken_burns_out",

        ImageAnimationMode.ZoomPulse => "zoom_pulse",

        ImageAnimationMode.DiagonalDr => "diagonal_dr",

        ImageAnimationMode.DiagonalDl => "diagonal_dl",

        ImageAnimationMode.Static => "static",

        ImageAnimationMode.Random => "random",

        _ => "zoom_in"

    };



    public static double NormalizeMotionSpeed(double? speed) =>

        speed is > 0 and <= 0.08 ? speed.Value : ImageMotionDefaults.MotionSpeed;



    public static double NormalizeMaxZoom(double? zoom) =>

        zoom is >= 1.0 and <= 2.5 ? zoom.Value : ImageMotionDefaults.MaxZoom;



    public static bool IsOversizePanMode(string mode) =>

        mode is "oversize_pan_right" or "oversize_pan_left" or "oversize_pan_up" or "oversize_pan_down";

    public static bool IsVideoClip(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".mov" or ".webm" or ".mkv";
    }

}

