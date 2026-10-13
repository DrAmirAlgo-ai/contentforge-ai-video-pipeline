using System.Text.RegularExpressions;
using ContentEmpireSystem.Core.DTOs;

namespace ContentEmpireSystem.Core;

/// <summary>Keeps TTS narration within the duration budget so Wan clips are not time-stretched.</summary>
public static partial class NarrationWordLimiter
{
    public static string Enforce(string text, int maxWords)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var trimmed = text.Trim();
        if (maxWords <= 0)
            return trimmed;

        var words = SplitWords(trimmed);
        if (words.Count <= maxWords)
            return trimmed;

        return string.Join(" ", words.Take(maxWords));
    }

    public static int ResolveMaxWords(ScriptOutputDto script, double wordsPerSecond, double sceneDurationSec)
    {
        var plan = script.ProductionPlan;
        var segments = Math.Max(
            1,
            plan?.ImageSegmentCount ?? script.ImageSuggestions.Count);
        if (segments <= 0)
            segments = 1;

        var durationBudget = ScriptGenerationHelper.WordsForDuration(sceneDurationSec * segments, wordsPerSecond);
        if (plan?.TargetWordCount is > 0)
            return Math.Min(plan.TargetWordCount, durationBudget);

        return durationBudget;
    }

    public static int CountWords(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : SplitWords(text.Trim()).Count;

    private static List<string> SplitWords(string text) =>
        WordSplit().Split(text)
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .ToList();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WordSplit();
}
