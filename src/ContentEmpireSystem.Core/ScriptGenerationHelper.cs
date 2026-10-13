using ContentEmpireSystem.Core.DTOs;

namespace ContentEmpireSystem.Core;



public static class ScriptGenerationHelper

{

    public const int MinWordCount = 1;

    public const int MaxWordCount = 10000;

    public const int DefaultWordCount = 300;

    /// <summary>Max tokens for single-pass narration JSON (one LLM call).</summary>
    public const int SinglePassMaxOutputTokens = 8192;

    public static void ApplySceneCountsFromSettings(
        ScriptProductionPlanDto plan, int segmentCount, string? workflowPath)
    {
        var segments = ClampSegmentCount(segmentCount);
        if (ProductionPlanResolver.IsTextToVideo(workflowPath))
        {
            plan.ImageSegmentCount = 0;
            plan.VideoSegmentCount = segments;
        }
        else
        {
            plan.ImageSegmentCount = segments;
            plan.VideoSegmentCount = segments;
        }
    }

    public const int MinSegmentCount = 1;

    public const int MaxSegmentCount = 160;

    public const int DefaultImageSegmentCount = 8;

    public const int DefaultVideoSegmentCount = 8;



    /// <summary>Approximate spoken words for Arabic/English narration (~2.5 words/sec).</summary>

    private const double WordsPerSecond = 2.5;



    /// <summary>Derived duration bounds (used for audio timing / legacy display).</summary>

    public const int MinDurationSec = 30;

    public const int MaxDurationSec = 600;

    public const int DefaultDurationSec = 120;



    public static int MaxSegmentCountLimit => MaxSegmentCount;



    public static int ClampWordCount(int words) =>

        Math.Clamp(words, MinWordCount, MaxWordCount);



    public static int ClampSegmentCount(int count) =>

        Math.Clamp(count, MinSegmentCount, MaxSegmentCount);



    public static int EstimateDurationSec(int wordCount) =>

        (int)Math.Round(ClampWordCount(wordCount) / WordsPerSecond);



    public static int EstimateWordCount(int durationSec) =>

        (int)Math.Round(Math.Clamp(durationSec, MinDurationSec, MaxDurationSec) * WordsPerSecond);

    /// <summary>Word budget for a short scene clip (no 30s minimum).</summary>
    public static int WordsForDuration(double durationSec, double wordsPerSecond = WordsPerSecond) =>
        Math.Max(1, (int)Math.Round(Math.Max(0.5, durationSec) * Math.Clamp(wordsPerSecond, 0.5, 10)));



    public static int ResolveMaxDurationSec(string? configuredSeconds) =>
        int.TryParse(configuredSeconds, out var sec) && sec > 0
            ? ClampDurationSec(sec)
            : MaxDurationSec;

    public static int ClampDurationSec(int seconds) =>

        Math.Clamp(seconds, MinDurationSec, MaxDurationSec);



    public static double SegmentDurationSec(int wordCount, int segmentCount)

    {

        if (segmentCount <= 0)

            return 15;



        return EstimateDurationSec(wordCount) / (double)segmentCount;

    }



    public static int SegmentDurationMs(int wordCount, int segmentCount) =>

        (int)Math.Round(SegmentDurationSec(wordCount, segmentCount) * 1000);

}

