using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Entities;

namespace ContentEmpireSystem.Core;

public static class VoiceCodeTimingHelper
{
    public const int MaxVoiceSegments = 20;
    public const int MaxVisualPartsPerSegment = 2;

    public static void ApplyTimings(
        IList<GeneratedImage> mediaSegments,
        IReadOnlyList<VoiceSegmentDto> voiceSegments,
        double audioDurationSec)
    {
        if (voiceSegments.Count == 0 || audioDurationSec <= 0)
            return;

        var orderedVoice = voiceSegments.OrderBy(v => v.SortOrder).ToList();
        var wordCounts = orderedVoice.Select(v => CountWords(v.Text)).ToList();
        var totalWords = wordCounts.Sum();

        var mediaByVoice = mediaSegments
            .GroupBy(m => m.SortOrder)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.VisualPartIndex).ToList());

        var cursorMs = 0d;
        for (var i = 0; i < orderedVoice.Count; i++)
        {
            var voiceSeg = orderedVoice[i];
            var durationMs = totalWords > 0
                ? audioDurationSec * 1000d * (wordCounts[i] / (double)totalWords)
                : audioDurationSec * 1000d / orderedVoice.Count;

            var voiceStartMs = cursorMs;

            if (mediaByVoice.TryGetValue(voiceSeg.SortOrder, out var parts) && parts.Count > 0)
            {
                var partDurationMs = durationMs / parts.Count;
                for (var p = 0; p < parts.Count; p++)
                {
                    parts[p].StartTimeMs = voiceStartMs + p * partDurationMs;
                    parts[p].EndTimeMs = voiceStartMs + (p + 1) * partDurationMs;
                }
            }

            cursorMs += durationMs;
        }

        var flatOrdered = mediaSegments
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.VisualPartIndex)
            .ToList();
        if (flatOrdered.Count > 0)
            flatOrdered[^1].EndTimeMs = audioDurationSec * 1000d;
    }

    public static List<GeneratedImage> OrderForPlayback(IEnumerable<GeneratedImage> segments) =>
        segments.OrderBy(s => s.SortOrder).ThenBy(s => s.VisualPartIndex).ToList();

    private static int CountWords(string text) =>
        text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
}
