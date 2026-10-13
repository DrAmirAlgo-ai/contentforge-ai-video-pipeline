using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Entities;

namespace ContentEmpireSystem.Infrastructure.Services;

internal static class MediaDurationHelper
{
    private static readonly Regex DurationRegex = new(
        @"Duration:\s*(\d{2}):(\d{2}):(\d{2}\.\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool ParseForceSync(string? value) =>
        !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase);

    public static async Task<double> ProbeDurationSecondsAsync(
        string ffmpegExecutable, string mediaPath, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegExecutable,
            Arguments = $"-hide_banner -i \"{mediaPath}\"",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"FFmpeg not found at '{ffmpegExecutable}'");

        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var match = DurationRegex.Match(stderr);
        if (!match.Success)
            throw new InvalidOperationException($"Could not probe media duration: {mediaPath}");

        var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var seconds = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        return hours * 3600d + minutes * 60d + seconds;
    }

    public static List<WordTimestampDto> ScaleWordTimestampsToDuration(
        List<WordTimestampDto> words, double targetDurationMs)
    {
        if (words.Count == 0 || targetDurationMs <= 0)
            return words;

        var sourceEnd = words[^1].EndMs;
        if (sourceEnd <= 0)
            return words;

        var scale = targetDurationMs / sourceEnd;
        if (Math.Abs(scale - 1.0) < 0.005)
            return words;

        return words.Select(w => new WordTimestampDto
        {
            Word = w.Word,
            StartMs = w.StartMs * scale,
            EndMs = w.EndMs * scale
        }).ToList();
    }

    public static void DistributeEqualImageTimings(
        IList<GeneratedImage> images, double totalDurationMs)
    {
        if (images.Count == 0 || totalDurationMs <= 0)
            return;

        var segmentDuration = totalDurationMs / images.Count;
        for (var i = 0; i < images.Count; i++)
        {
            images[i].StartTimeMs = i * segmentDuration;
            images[i].EndTimeMs = (i + 1) * segmentDuration;
        }
    }

    public static double GetTotalClipDurationMs(IEnumerable<GeneratedImage> clips) =>
        clips.Sum(c => Math.Max(0, c.EndTimeMs - c.StartTimeMs));
}
