namespace ContentEmpireSystem.Core;

using ContentEmpireSystem.Core.DTOs;

/// <summary>Splits very long narration targets into LLM-sized chunks.</summary>
public static class LongScriptNarrationPlanner
{
    public const int NarrationChunkWordCount = 1500;
    public const int ChunkedGenerationThreshold = 1500;
    public const int ContextTailWordCount = 200;
    public const int MaxExtensionAttempts = 5;
    /// <summary>Legacy ratio helpers — generation no longer rejects or trims by these bounds.</summary>
    public const double MinimumCompletionRatio = 0;
    public const double MaximumCompletionRatio = double.PositiveInfinity;

    public static bool RequiresChunkedNarration(int wordTarget) =>
        wordTarget >= ChunkedGenerationThreshold;

    public static IReadOnlyList<int> BuildChunkWordTargets(int totalWords)
    {
        var remaining = ScriptGenerationHelper.ClampWordCount(totalWords);
        var chunks = new List<int>();

        while (remaining > 0)
        {
            var take = Math.Min(NarrationChunkWordCount, remaining);
            chunks.Add(take);
            remaining -= take;
        }

        return chunks;
    }

    public static int CountWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return SplitWords(text).Length;
    }

    public static string TailForContext(string text, int maxWords = ContextTailWordCount)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var words = SplitWords(text);
        if (words.Length <= maxWords)
            return text.Trim();

        return string.Join(' ', words[^maxWords..]);
    }

    public static bool MeetsWordTarget(string? narration, int wordTarget) =>
        CountWords(narration) > 0;

    public static bool ExceedsWordTarget(string? narration, int wordTarget) => false;

    public static bool IsWithinWordTarget(string? narration, int wordTarget) =>
        CountWords(narration) > 0;

    public static int MinimumWords(int wordTarget) => 1;

    public static int MaximumWords(int wordTarget) => int.MaxValue;

    /// <summary>Trims narration to at most <paramref name="wordTarget"/> words (sentence-aware when possible).</summary>
    public static string TrimToWordTarget(string? narration, int wordTarget)
    {
        if (string.IsNullOrWhiteSpace(narration))
            return narration ?? "";

        var words = SplitWords(narration);
        if (words.Length <= wordTarget)
            return narration.Trim();

        var candidate = string.Join(' ', words.Take(wordTarget));
        var lastBreak = FindLastSentenceBreakIndex(candidate);
        if (lastBreak > 0)
        {
            var sentenceTrimmed = candidate[..lastBreak].TrimEnd();
            if (CountWords(sentenceTrimmed) >= MinimumWords(wordTarget))
                return sentenceTrimmed;
        }

        return candidate;
    }

    public static void EnforceWordTarget(ScriptOutputDto script, int wordTarget)
    {
    }

    public static string CapChunkText(string? chunkText, int chunkWordTarget) =>
        chunkText?.Trim() ?? "";

    private static string[] SplitWords(string text) =>
        text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);

    private static int FindLastSentenceBreakIndex(string text)
    {
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (text[i] is '.' or '!' or '?' or '…' or '؟')
                return i + 1;
        }

        return -1;
    }
}
