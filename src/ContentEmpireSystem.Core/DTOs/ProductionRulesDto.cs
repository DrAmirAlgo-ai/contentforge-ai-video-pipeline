using ContentEmpireSystem.Core.Enums;

namespace ContentEmpireSystem.Core.DTOs;

/// <summary>
/// Effective production limits — from DB settings with code defaults as fallback.
/// </summary>
public sealed class ProductionRulesDto
{
    public int MinSegmentsForProduce { get; init; } = ProductionRules.MinSegmentsForProduce;
    public int DefaultImageSegmentCount { get; init; } = ScriptGenerationHelper.DefaultImageSegmentCount;
    public int DefaultWordCount { get; init; } = ProductionRules.DefaultWordCount;
    public int MinWordCount { get; init; } = ProductionRules.MinWordCount;
    public int MaxWordCount { get; init; } = ProductionRules.MaxWordCount;
    public int MaxVideoDurationSec { get; init; } = ProductionRules.MaxVideoDurationSec;
    public double WordsPerSecond { get; init; } = 2.5;

    public static ProductionRulesDto Defaults { get; } = new();

    public int ClampWordCount(int words) =>
        Math.Clamp(words, MinWordCount, MaxWordCount);

    public int EstimateDurationSec(int wordCount) =>
        (int)Math.Round(ClampWordCount(wordCount) / WordsPerSecond);

    public bool MeetsMinimumSegmentCount(int count) => count >= MinSegmentsForProduce;

    public static ProductionRulesDto FromSettings(IReadOnlyDictionary<string, string> settings)
    {
        var d = Defaults;
        return new ProductionRulesDto
        {
            MinSegmentsForProduce = ParseInt(settings, SettingKeys.ProductionMinSegmentsForProduce, d.MinSegmentsForProduce, 1, 50),
            DefaultImageSegmentCount = ParseInt(settings, SettingKeys.ProductionDefaultImageSegmentCount, d.DefaultImageSegmentCount,
                ScriptGenerationHelper.MinSegmentCount, ScriptGenerationHelper.MaxSegmentCount),
            DefaultWordCount = ParseInt(settings, SettingKeys.ProductionDefaultWordCount, d.DefaultWordCount, d.MinWordCount, d.MaxWordCount),
            MinWordCount = ParseInt(settings, SettingKeys.ProductionMinWordCount, d.MinWordCount, 1, 50000),
            MaxWordCount = ParseInt(settings, SettingKeys.ProductionMaxWordCount, d.MaxWordCount, 75, 50000),
            MaxVideoDurationSec = ParseInt(settings, SettingKeys.MaxVideoDurationSec, d.MaxVideoDurationSec, 15, 3600),
            WordsPerSecond = ParseDouble(settings, SettingKeys.ProductionWordsPerSecond, d.WordsPerSecond, 0.5, 10),
        };
    }

    private static int ParseInt(IReadOnlyDictionary<string, string> settings, string key, int fallback, int min, int max)
    {
        if (!settings.TryGetValue(key, out var raw) || !int.TryParse(raw, out var value))
            return fallback;
        return Math.Clamp(value, min, max);
    }

    private static double ParseDouble(IReadOnlyDictionary<string, string> settings, string key, double fallback, double min, double max)
    {
        if (!settings.TryGetValue(key, out var raw) || !double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            return fallback;
        return Math.Clamp(value, min, max);
    }
}
