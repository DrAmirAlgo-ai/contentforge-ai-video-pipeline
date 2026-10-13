namespace ContentEmpireSystem.Core;

public static class FluxQualityPresets
{
    public const string Fast = "fast";
    public const string Balanced = "balanced";
    public const string High = "high";
    public const string Ultra = "ultra";
    public const string Custom = "custom";

    public const string Default = Balanced;

    public sealed record Values(int Width, int Height, int Steps, string LabelAr);

    private static readonly Dictionary<string, Values> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [Fast] = new(512, 896, 6, "سريع — 512×896"),
        [Balanced] = new(640, 1152, 8, "متوازن — 640×1152"),
        [High] = new(768, 1344, 12, "عالي — 768×1344"),
        [Ultra] = new(832, 1472, 16, "أقصى — 832×1472 (8GB+)"),
    };

    public static string Normalize(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? Default
        : Map.ContainsKey(raw.Trim()) ? raw.Trim().ToLowerInvariant()
        : Custom;

    public static Values Resolve(string? preset, int? manualWidth, int? manualHeight, int? manualSteps)
    {
        var key = Normalize(preset);
        if (key == Custom)
        {
            return new Values(
                manualWidth ?? Map[Balanced].Width,
                manualHeight ?? Map[Balanced].Height,
                manualSteps ?? Map[Balanced].Steps,
                "مخصص");
        }

        return Map[key];
    }

    public static IReadOnlyList<(string Id, string Label)> ListForUi() =>
        Map.Select(kv => (kv.Key, kv.Value.LabelAr)).Append((Custom, "مخصص — من الحقول التقنية")).ToList();
}
