using ContentEmpireSystem.Core.Entities;

namespace ContentEmpireSystem.Core;

public readonly record struct MotionAssignment(string Mode, double MotionSpeed, double MaxZoom);

public static class ImageMotionAutoAssigner
{
    private static readonly string[] VarietyPool =
    [
        "oversize_pan_right", "zoom_in", "ken_burns", "oversize_pan_left",
        "oversize_pan_up", "diagonal_dr", "zoom_out", "ken_burns_out",
        "oversize_pan_down", "zoom_pulse", "diagonal_dl", "pan_right",
        "pan_left", "pan_up", "pan_down",
    ];

    public static bool IsAutoMode(string? settingValue) =>
        !string.Equals(settingValue?.Trim(), "manual", StringComparison.OrdinalIgnoreCase);

    public static void Apply(IEnumerable<GeneratedImage> images)
    {
        var ordered = images
            .Where(i => !ImageMotionHelper.IsVideoClip(i.LocalFilePath))
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.VisualPartIndex)
            .ToList();

        if (ordered.Count == 0)
            return;

        string? previousMode = null;
        for (var i = 0; i < ordered.Count; i++)
        {
            var assignment = Assign(ordered[i], i, ordered.Count, previousMode);
            ordered[i].AnimationMode = assignment.Mode;
            ordered[i].MotionSpeed = assignment.MotionSpeed;
            ordered[i].MaxZoom = assignment.MaxZoom;
            previousMode = assignment.Mode;
        }
    }

    public static MotionAssignment Assign(GeneratedImage image, int index, int total, string? previousMode)
    {
        var text = $"{image.ScenePrompt} {image.Prompt}".ToLowerInvariant();
        var mode = InferFromContext(text, index, total)
            ?? InferFromPosition(index, total)
            ?? PickFromPool(index, text, previousMode);

        if (string.Equals(mode, previousMode, StringComparison.OrdinalIgnoreCase))
            mode = PickFromPool(index + 1, text, previousMode);

        mode = ImageMotionHelper.NormalizeMode(mode);
        return new MotionAssignment(mode, InferSpeed(mode, text), InferMaxZoom(mode, text));
    }

    private static string? InferFromContext(string text, int index, int total)
    {
        if (ContainsAny(text, "wide", "landscape", "panorama", "skyline", "cityscape", "street", "horizon",
                "منظر", "أفق", "شارع", "مدينة"))
            return index % 2 == 0 ? "oversize_pan_right" : "oversize_pan_left";

        if (ContainsAny(text, "portrait", "face", "person", "selfie", "close-up", "closeup", "eyes",
                "وجه", "شخص", "بورتريه", "قريب"))
            return index % 2 == 0 ? "zoom_in" : "ken_burns";

        if (ContainsAny(text, "tower", "building", "skyscraper", "mountain", "tall", "vertical",
                "برج", "مبنى", "جبل"))
            return "oversize_pan_up";

        if (ContainsAny(text, "water", "river", "ocean", "sea", "lake", "flow", "wave",
                "ماء", "نهر", "بحر", "موج"))
            return index % 2 == 0 ? "oversize_pan_right" : "oversize_pan_left";

        if (ContainsAny(text, "action", "run", "running", "chase", "fight", "dynamic", "speed", "race",
                "حركة", "جري", "مطاردة", "سرعة"))
            return index % 2 == 0 ? "diagonal_dr" : "ken_burns";

        if (ContainsAny(text, "group", "crowd", "market", "team", "people", "audience",
                "جمهور", "حشد", "فريق", "ناس"))
            return "pan_right";

        if (ContainsAny(text, "calm", "peace", "sunset", "sunrise", "ending", "final", "quiet",
                "هادئ", "غروب", "نهاية"))
            return index >= total - 1 ? "zoom_out" : "ken_burns_out";

        return null;
    }

    private static string? InferFromPosition(int index, int total)
    {
        if (index == 0)
            return "oversize_pan_right";

        if (index == 1)
            return "zoom_in";

        if (total > 2 && index == total - 1)
            return "zoom_out";

        if (total > 3 && index == total - 2)
            return "ken_burns_out";

        return null;
    }

    private static string PickFromPool(int index, string text, string? previousMode)
    {
        var seed = StableHash(text) + index * 17;
        for (var offset = 0; offset < VarietyPool.Length; offset++)
        {
            var candidate = VarietyPool[(seed + offset) % VarietyPool.Length];
            if (!string.Equals(candidate, previousMode, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return VarietyPool[index % VarietyPool.Length];
    }

    private static double InferSpeed(string mode, string text)
    {
        if (ContainsAny(text, "calm", "slow", "peace", "هادئ", "بطيء"))
            return 0.0015;

        if (ContainsAny(text, "action", "fast", "dynamic", "سريع", "حركة"))
            return 0.003;

        return mode switch
        {
            "zoom_pulse" => 0.0025,
            "ken_burns" or "ken_burns_out" => 0.0022,
            var m when m.StartsWith("oversize_pan", StringComparison.Ordinal) => 0.002,
            _ => ImageMotionDefaults.MotionSpeed,
        };
    }

    private static double InferMaxZoom(string mode, string text)
    {
        if (ContainsAny(text, "detail", "close", "macro", "تفصيل", "قريب"))
            return 1.55;

        return mode switch
        {
            var m when m.StartsWith("oversize_pan", StringComparison.Ordinal) => 1.48,
            "zoom_in" or "ken_burns" => 1.38,
            "zoom_out" or "ken_burns_out" => 1.32,
            "zoom_pulse" => 1.42,
            _ => ImageMotionDefaults.MaxZoom,
        };
    }

    private static bool ContainsAny(string text, params string[] terms)
    {
        foreach (var term in terms)
        {
            if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static int StableHash(string text)
    {
        unchecked
        {
            var hash = 23;
            foreach (var ch in text)
                hash = (hash * 31) + ch;
            return Math.Abs(hash);
        }
    }
}
