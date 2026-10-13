namespace ContentEmpireSystem.Core;

/// <summary>
/// Legacy code defaults — prefer <see cref="ProductionRulesDto.FromSettings"/> at runtime.
/// </summary>
public static class ProductionRules
{
    /// <summary>Fallback when DB settings are unavailable (mirrors ProductionRulesDto.Defaults).</summary>
    public const int MinSegmentsForProduce = 3;

    public static int MinWordCount => ScriptGenerationHelper.MinWordCount;
    public static int MaxWordCount => ScriptGenerationHelper.MaxWordCount;
    public static int DefaultWordCount => ScriptGenerationHelper.DefaultWordCount;
    public static int MaxVideoDurationSec => ScriptGenerationHelper.MaxDurationSec;

    public static int ClampWordCount(int words) => ScriptGenerationHelper.ClampWordCount(words);

    public static int EstimateDurationSec(int wordCount) => ScriptGenerationHelper.EstimateDurationSec(wordCount);

    public static bool MeetsMinimumSegmentCount(int count) => count >= MinSegmentsForProduce;
}
