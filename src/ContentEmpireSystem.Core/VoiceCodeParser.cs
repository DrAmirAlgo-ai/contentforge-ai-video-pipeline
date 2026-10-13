using System.Text.RegularExpressions;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Entities;

namespace ContentEmpireSystem.Core;

public static partial class VoiceCodeParser
{
    [GeneratedRegex(@"\[VC\s*:\s*(\d+)\s*\]", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex VoiceCodeMarkerRegex();

    public static bool ContainsVoiceCodeMarkers(string text) =>
        VoiceCodeMarkerRegex().IsMatch(text);

    public static List<VoiceSegmentDto> Parse(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            throw new InvalidOperationException("أدخل نص Voice Code أولاً.");

        var text = rawText.Trim();
        if (!ContainsVoiceCodeMarkers(text))
            throw new InvalidOperationException(
                "Voice Code يتطلب علامات [VC:1] [VC:2] ... لفصل المقاطع الصوتية.");

        var matches = VoiceCodeMarkerRegex().Matches(text);
        if (matches.Count < 3)
            throw new InvalidOperationException("Voice Code يحتاج 3 مقاطع على الأقل.");

        if (matches.Count > VoiceCodeTimingHelper.MaxVoiceSegments)
            throw new InvalidOperationException(
                $"Voice Code يدعم حتى {VoiceCodeTimingHelper.MaxVoiceSegments} مقطع فقط.");

        var segments = new List<VoiceSegmentDto>();
        for (var i = 0; i < matches.Count; i++)
        {
            var marker = matches[i];
            var declaredOrder = int.Parse(marker.Groups[1].Value);
            var contentStart = marker.Index + marker.Length;
            var contentEnd = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var segmentText = text[contentStart..contentEnd].Trim();

            if (string.IsNullOrWhiteSpace(segmentText))
                throw new InvalidOperationException($"المقطع [VC:{declaredOrder}] فارغ — أضف النص بعد العلامة.");

            segments.Add(new VoiceSegmentDto
            {
                SortOrder = declaredOrder,
                Label = $"مقطع {declaredOrder}",
                Text = segmentText,
                VisualParts =
                [
                    new VoiceVisualPartDto { PartIndex = 0 },
                    new VoiceVisualPartDto { PartIndex = 1 }
                ]
            });
        }

        var orders = segments.Select(s => s.SortOrder).ToList();
        if (orders.Distinct().Count() != orders.Count)
            throw new InvalidOperationException("أرقام مقاطع Voice Code مكررة — استخدم [VC:1] [VC:2] ... بدون تكرار.");

        return segments.OrderBy(s => s.SortOrder).ToList();
    }

    public static string BuildFullNarration(IEnumerable<VoiceSegmentDto> segments) =>
        string.Join(" ", segments.OrderBy(s => s.SortOrder).Select(s => s.Text.Trim()).Where(t => t.Length > 0));

    public static string BuildRawText(IEnumerable<VoiceSegmentDto> segments) =>
        string.Join("\n\n", segments.OrderBy(s => s.SortOrder).Select(s => $"[VC:{s.SortOrder}]\n{s.Text.Trim()}"));

    public static void SyncMediaFlags(ScriptOutputDto script, IEnumerable<GeneratedImage> images)
    {
        if (!script.IsVoiceCode)
            return;

        var imageList = images.ToList();
        var byVoice = imageList
            .GroupBy(i => i.SortOrder)
            .ToDictionary(g => g.Key, g => g.OrderBy(i => i.VisualPartIndex).ToList());

        foreach (var seg in script.VoiceSegments)
        {
            seg.VisualParts =
            [
                new VoiceVisualPartDto { PartIndex = 0 },
                new VoiceVisualPartDto { PartIndex = 1 }
            ];

            if (!byVoice.TryGetValue(seg.SortOrder, out var parts))
            {
                seg.HasMedia = false;
                seg.IsVideoClip = false;
                seg.MediaId = null;
                continue;
            }

            foreach (var part in parts)
            {
                if (part.VisualPartIndex is < 0 or >= VoiceCodeTimingHelper.MaxVisualPartsPerSegment)
                    continue;

                if (string.IsNullOrWhiteSpace(part.LocalFilePath))
                    continue;

                var dto = seg.VisualParts[part.VisualPartIndex];
                dto.HasMedia = true;
                dto.IsVideoClip = ImageMotionHelper.IsVideoClip(part.LocalFilePath);
                dto.MediaId = part.Id;
            }

            var primary = seg.VisualParts.FirstOrDefault(p => p.HasMedia);
            seg.HasMedia = primary?.HasMedia ?? false;
            seg.IsVideoClip = primary?.IsVideoClip ?? false;
            seg.MediaId = primary?.MediaId;
        }
    }

    public static int CountSegmentsWithMedia(ScriptOutputDto script, IEnumerable<GeneratedImage> images)
    {
        if (!script.IsVoiceCode)
            return 0;

        var imageList = images.ToList();
        return script.VoiceSegments.Count(seg =>
            imageList.Any(i =>
                i.SortOrder == seg.SortOrder
                && !string.IsNullOrWhiteSpace(i.LocalFilePath)));
    }
}
