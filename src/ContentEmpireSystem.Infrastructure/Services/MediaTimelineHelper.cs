using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Entities;

namespace ContentEmpireSystem.Infrastructure.Services;

public readonly record struct TimelineEntry(int SourceSegmentIndex, double DurationSec);

internal static class MediaTimelineHelper
{
    public static List<double> GetBaseSegmentDurations(
        IList<GeneratedImage> segments,
        MontageSettings montage,
        bool nativeClipDurations,
        IReadOnlyList<double>? probedFileDurationsSec = null)
    {
        if (nativeClipDurations)
        {
            if (probedFileDurationsSec is not null && probedFileDurationsSec.Count == segments.Count)
            {
                return probedFileDurationsSec
                    .Select(d => d > 0.01 ? d : montage.ImageDurationSec)
                    .ToList();
            }

            return segments
                .Select(seg =>
                {
                    var sec = (seg.EndTimeMs - seg.StartTimeMs) / 1000.0;
                    return sec > 0.01 ? sec : montage.ImageDurationSec;
                })
                .ToList();
        }

        if (montage.ImageTimingMode == "fixed_duration")
            return segments.Select(_ => montage.ImageDurationSec).ToList();

        return segments
            .Select(seg =>
            {
                var sec = (seg.EndTimeMs - seg.StartTimeMs) / 1000.0;
                return sec > 0.01 ? sec : montage.ImageDurationSec;
            })
            .ToList();
    }

    public static List<TimelineEntry> BuildTimeline(
        IList<GeneratedImage> segments,
        MontageSettings montage,
        double audioDurationSec,
        bool nativeClipDurations,
        IReadOnlyList<double>? probedFileDurationsSec = null)
    {
        if (segments.Count == 0)
            return [];

        var baseDurations = GetBaseSegmentDurations(segments, montage, nativeClipDurations, probedFileDurationsSec);

        return montage.SyncMode switch
        {
            "media_master" => ToTimeline(baseDurations),
            "shortest" => ToTimeline(baseDurations),
            _ => ResolveAudioMasterTimeline(baseDurations, audioDurationSec, montage)
        };
    }

    public static double ResolveOutputDurationSec(
        MontageSettings montage,
        double audioDurationSec,
        List<TimelineEntry> timeline)
    {
        var mediaDurationSec = timeline.Sum(t => t.DurationSec);

        return montage.SyncMode switch
        {
            "media_master" => mediaDurationSec,
            "shortest" => Math.Min(audioDurationSec, mediaDurationSec),
            _ => audioDurationSec
        };
    }

    public static double ComputeMergedVideoDurationSec(
        IReadOnlyList<double> segmentDurationsSec,
        MontageSettings montage)
    {
        if (segmentDurationsSec.Count == 0)
            return 0;

        if (segmentDurationsSec.Count == 1)
            return segmentDurationsSec[0];

        if (montage.ResolveXfadeTransition() is null || montage.TransitionDurationSec <= 0)
            return segmentDurationsSec.Sum();

        var total = segmentDurationsSec[0];
        for (var i = 1; i < segmentDurationsSec.Count; i++)
            total += segmentDurationsSec[i] - montage.TransitionDurationSec;

        return total;
    }

    public static List<TimelineEntry> CompensateForTransitionOverlap(
        List<TimelineEntry> timeline,
        MontageSettings montage,
        double targetOutputDurationSec)
    {
        if (timeline.Count <= 1)
            return timeline;

        var segmentDurations = timeline.Select(t => t.DurationSec).ToList();
        var mergedDuration = ComputeMergedVideoDurationSec(segmentDurations, montage);
        var deficit = targetOutputDurationSec - mergedDuration;

        if (deficit <= 0.01)
            return timeline;

        var result = timeline.ToList();
        var last = result[^1];
        result[^1] = last with { DurationSec = last.DurationSec + deficit };
        return result;
    }

    public static List<WordTimestampDto> ApplySubtitleSync(
        List<WordTimestampDto> words,
        MontageSettings montage,
        double audioDurationSec,
        double videoDurationSec)
    {
        if (words.Count == 0)
            return words;

        var synced = montage.SubtitleSyncMode switch
        {
            "native" => words,
            "scale_to_video" => MediaDurationHelper.ScaleWordTimestampsToDuration(words, videoDurationSec * 1000d),
            _ => MediaDurationHelper.ScaleWordTimestampsToDuration(words, audioDurationSec * 1000d)
        };

        if (Math.Abs(montage.SubtitleOffsetMs) < 0.5)
            return synced;

        return synced.Select(w => new WordTimestampDto
        {
            Word = w.Word,
            StartMs = Math.Max(0, w.StartMs + montage.SubtitleOffsetMs),
            EndMs = Math.Max(0, w.EndMs + montage.SubtitleOffsetMs)
        }).ToList();
    }

    public static void AssignImageTimingsFromTimeline(
        IList<GeneratedImage> images,
        List<TimelineEntry> timeline)
    {
        if (images.Count == 0 || timeline.Count == 0)
            return;

        var cursorMs = 0d;
        var assigned = new Dictionary<int, (double StartMs, double EndMs)>();

        foreach (var entry in timeline)
        {
            var durationMs = entry.DurationSec * 1000d;
            if (!assigned.TryGetValue(entry.SourceSegmentIndex, out var range))
                assigned[entry.SourceSegmentIndex] = (cursorMs, cursorMs + durationMs);
            else
                assigned[entry.SourceSegmentIndex] = (range.StartMs, range.EndMs + durationMs);

            cursorMs += durationMs;
        }

        foreach (var (index, range) in assigned)
            images[index].StartTimeMs = range.StartMs;
        foreach (var (index, range) in assigned)
            images[index].EndTimeMs = range.EndMs;
    }

    private static List<TimelineEntry> ResolveAudioMasterTimeline(
        List<double> baseDurations,
        double audioDurationSec,
        MontageSettings montage)
    {
        var totalMedia = baseDurations.Sum();

        if (totalMedia >= audioDurationSec - 0.05)
        {
            if (montage.MediaOverflowAction == "keep")
                return ToTimeline(baseDurations);

            return TrimTimelineToDuration(baseDurations, audioDurationSec);
        }

        return montage.MediaShortfallAction switch
        {
            "loop_sequence" => LoopSequenceTimeline(baseDurations, audioDurationSec),
            "loop_last" => ExtendLastTimeline(baseDurations, audioDurationSec),
            "freeze_last" => ExtendLastTimeline(baseDurations, audioDurationSec),
            _ => StretchTimeline(baseDurations, audioDurationSec, montage.ImageTimingMode)
        };
    }

    private static List<TimelineEntry> StretchTimeline(
        List<double> baseDurations,
        double audioDurationSec,
        string imageTimingMode)
    {
        if (imageTimingMode == "fixed_duration")
        {
            var scale = audioDurationSec / Math.Max(baseDurations.Sum(), 0.001);
            return baseDurations
                .Select((duration, index) => new TimelineEntry(index, duration * scale))
                .ToList();
        }

        var equalDuration = audioDurationSec / baseDurations.Count;
        return baseDurations
            .Select((_, index) => new TimelineEntry(index, equalDuration))
            .ToList();
    }

    private static List<TimelineEntry> LoopSequenceTimeline(
        List<double> baseDurations,
        double audioDurationSec)
    {
        var timeline = new List<TimelineEntry>();
        var accumulated = 0d;

        while (accumulated < audioDurationSec - 0.01 && baseDurations.Count > 0)
        {
            for (var i = 0; i < baseDurations.Count; i++)
            {
                var remaining = audioDurationSec - accumulated;
                if (remaining <= 0.01)
                    break;

                var duration = Math.Min(baseDurations[i], remaining);
                timeline.Add(new TimelineEntry(i, duration));
                accumulated += duration;
            }
        }

        return timeline;
    }

    private static List<TimelineEntry> ExtendLastTimeline(
        List<double> baseDurations,
        double audioDurationSec)
    {
        if (baseDurations.Count == 0)
            return [];

        var timeline = new List<TimelineEntry>();
        var accumulated = 0d;

        for (var i = 0; i < baseDurations.Count; i++)
        {
            if (i == baseDurations.Count - 1)
            {
                timeline.Add(new TimelineEntry(i, Math.Max(0.05, audioDurationSec - accumulated)));
                break;
            }

            timeline.Add(new TimelineEntry(i, baseDurations[i]));
            accumulated += baseDurations[i];
        }

        return timeline;
    }

    private static List<TimelineEntry> TrimTimelineToDuration(
        List<double> baseDurations,
        double targetDurationSec)
    {
        var timeline = new List<TimelineEntry>();
        var accumulated = 0d;

        for (var i = 0; i < baseDurations.Count; i++)
        {
            var remaining = targetDurationSec - accumulated;
            if (remaining <= 0.01)
                break;

            var duration = Math.Min(baseDurations[i], remaining);
            timeline.Add(new TimelineEntry(i, duration));
            accumulated += duration;
        }

        return timeline;
    }

    private static List<TimelineEntry> ToTimeline(List<double> baseDurations) =>
        baseDurations.Select((duration, index) => new TimelineEntry(index, duration)).ToList();

    public static List<TimelineEntry> BuildPresetTimeline(IList<GeneratedImage> segments)
    {
        if (segments.Count == 0)
            return [];

        return VoiceCodeTimingHelper.OrderForPlayback(segments)
            .Select((segment, index) =>
            {
                var durationSec = (segment.EndTimeMs - segment.StartTimeMs) / 1000.0;
                return new TimelineEntry(index, durationSec > 0.01 ? durationSec : 0.05);
            })
            .ToList();
    }
}
