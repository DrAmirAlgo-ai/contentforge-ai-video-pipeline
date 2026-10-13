using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Entities;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContentEmpireSystem.Infrastructure.Services;

public class FfmpegVideoService(
    ApplicationDbContext db,
    ISystemSettingsService settings,
    IOutputPathService paths,
    IWorkloadGate workloadGate) : IFfmpegVideoService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<string> RenderVideoAsync(Guid projectId, CancellationToken ct = default)
    {
        // Global FFmpeg limit only — project lock is held by the production pipeline.
        await using var gate = await workloadGate.AcquireAsync(WorkloadSlot.Ffmpeg, projectId: null, ct);
        return await RenderVideoCoreAsync(projectId, ct);
    }

    private async Task<string> RenderVideoCoreAsync(Guid projectId, CancellationToken ct)
    {
        var project = await db.VideoProjects
            .Include(p => p.GeneratedImages)
            .FirstAsync(p => p.Id == projectId, ct);

        if (string.IsNullOrWhiteSpace(project.AudioFilePath))
            throw new InvalidOperationException("Audio file path is not set.");

        paths.EnsureFileExists(project.AudioFilePath, "Audio file");

        var montage = await MontageSettings.LoadAsync(settings, ct);
        var ffmpeg = FfmpegLocator.Resolve(montage.FfmpegPath);
        var outputDir = paths.GetProjectDirectory(montage.OutputDirectory, projectId);

        var audioPath = Path.GetFullPath(paths.ToAbsolutePath(project.AudioFilePath));
        var audioDurationSec = await MediaDurationHelper.ProbeDurationSecondsAsync(ffmpeg, audioPath, ct);

        var isManualVideoClips = string.Equals(project.ImageProviderUsed, "ManualVideo", StringComparison.OrdinalIgnoreCase);
        var isVoiceCode = string.Equals(project.ImageProviderUsed, "VoiceCode", StringComparison.OrdinalIgnoreCase);
        var segmentsForSync = project.GeneratedImages.OrderBy(i => i.SortOrder).ToList();
        var isVideoProject = isManualVideoClips
            || (!isVoiceCode && segmentsForSync.All(s => MediaSegmentHelper.IsVideoClip(s.LocalFilePath)));

        if (montage.SyncMode == "audio_master" && !isVoiceCode)
            await SyncProjectTimingsToAudioAsync(project, audioDurationSec, montage, isVideoProject, ct);

        project = await db.VideoProjects
            .Include(p => p.GeneratedImages)
            .FirstAsync(p => p.Id == projectId, ct);

        var segments = isVoiceCode
            ? VoiceCodeTimingHelper.OrderForPlayback(project.GeneratedImages)
            : project.GeneratedImages.OrderBy(i => i.SortOrder).ToList();
        if (segments.Count == 0)
            throw new InvalidOperationException("No media segments for video rendering.");

        foreach (var seg in segments)
        {
            if (string.IsNullOrWhiteSpace(seg.LocalFilePath))
                throw new InvalidOperationException($"Segment {seg.SortOrder} has no local file path.");

            var abs = Path.GetFullPath(paths.ToAbsolutePath(seg.LocalFilePath));
            if (!File.Exists(abs) || new FileInfo(abs).Length == 0)
                throw new InvalidOperationException($"Segment {seg.SortOrder} not found or empty: {abs}");
        }

        var videoMode = !isVoiceCode && (isManualVideoClips
            || segments.All(s => MediaSegmentHelper.IsVideoClip(s.LocalFilePath)));
        var imageMode = !isVoiceCode && !videoMode && segments.All(s => MediaSegmentHelper.IsImageClip(s.LocalFilePath));
        var mixedMode = isVoiceCode || (!videoMode && !imageMode);

        var probedNativeDurations = (videoMode || mixedMode)
            ? await ProbeSegmentFileDurationsAsync(ffmpeg, segments, ct)
            : null;

        List<TimelineEntry> timeline;
        if (isVoiceCode)
        {
            timeline = MediaTimelineHelper.BuildPresetTimeline(segments);
            if (timeline.Count == 0)
                throw new InvalidOperationException("Could not build Voice Code timeline.");
        }
        else
        {
            var nativeClipDurations = isManualVideoClips || videoMode;
            timeline = MediaTimelineHelper.BuildTimeline(
                segments, montage, audioDurationSec, nativeClipDurations, probedNativeDurations);
            if (timeline.Count == 0)
                throw new InvalidOperationException("Could not build media timeline for rendering.");
        }

        var videoDurationSec = isVoiceCode
            ? timeline.Sum(t => t.DurationSec)
            : MediaTimelineHelper.ResolveOutputDurationSec(montage, audioDurationSec, timeline);
        timeline = MediaTimelineHelper.CompensateForTransitionOverlap(timeline, montage, videoDurationSec);

        var assPath = Path.GetFullPath(Path.Combine(outputDir, $"subtitles_{Guid.NewGuid():N}.ass"));
        if (montage.SubtitlesEnabled)
        {
            await GenerateAssSubtitlesAsync(project, assPath, montage, audioDurationSec, videoDurationSec, ct);
            project.AssSubtitlePath = assPath;
            await db.SaveChangesAsync(ct);
        }

        var filterScript = Path.GetFullPath(Path.Combine(outputDir, "filter.txt"));
        var videoOutput = Path.GetFullPath(Path.Combine(outputDir, montage.QuickPreviewMode ? "preview_video.mp4" : "final_video.mp4"));
        var workOutput = Path.GetFullPath(Path.Combine(outputDir, montage.QuickPreviewMode ? "_preview_work.mp4" : "_final_work.mp4"));

        string? resolvedMusicPath = null;
        if (montage.BackgroundMusicEnabled && !string.IsNullOrWhiteSpace(montage.BackgroundMusicPath))
        {
            resolvedMusicPath = paths.ToAbsolutePath(montage.BackgroundMusicPath);
            if (!File.Exists(resolvedMusicPath))
                resolvedMusicPath = null;
        }

        var (mediaInputs, filterComplex) = mixedMode
            ? BuildMixedSegmentPipeline(segments, timeline, montage, assPath, resolvedMusicPath, videoDurationSec, probedNativeDurations!, stretchToFit: isVoiceCode)
            : videoMode
                ? BuildVideoClipPipeline(segments, timeline, montage, assPath, resolvedMusicPath, videoDurationSec, probedNativeDurations!)
                : BuildImagePipeline(segments, timeline, montage, assPath, resolvedMusicPath, videoDurationSec);

        var args = new StringBuilder();
        args.Append("-y ");
        args.Append(mediaInputs);
        args.Append($"-i \"{audioPath}\" ");
        if (resolvedMusicPath is not null)
            args.Append($"-i \"{resolvedMusicPath}\" ");

        await File.WriteAllTextAsync(filterScript, filterComplex, ct);

        args.Append($"-filter_complex_script \"{filterScript}\" -map \"[vout]\" -map \"[aout]\"");

        var preset = montage.QuickPreviewMode ? "ultrafast" : "fast";
        var crf = montage.QuickPreviewMode ? Math.Min(montage.VideoCrf + 6, 28) : montage.VideoCrf;
        args.Append(MobileVideoExporter.BuildMontageEncodeArgs(montage.OutputFps, preset, crf));

        if (montage.SyncMode == "shortest")
            args.Append(" -shortest");
        else
            args.Append($" -t {videoDurationSec.ToString(CultureInfo.InvariantCulture)}");

        args.Append($" \"{workOutput}\"");

        await File.WriteAllTextAsync(Path.Combine(outputDir, "ffmpeg_montage_cmd.txt"), args.ToString(), ct);
        await RunFfmpegAsync(ffmpeg, args.ToString(), outputDir, ct);

        var deliveryArgs = MobileVideoExporter.BuildDeliveryPassArgs(workOutput, videoOutput, montage.OutputFps, crf);
        await File.WriteAllTextAsync(Path.Combine(outputDir, "ffmpeg_delivery_cmd.txt"), deliveryArgs, ct);
        await RunFfmpegAsync(ffmpeg, deliveryArgs, outputDir, ct);

        TryDeleteFile(workOutput);
        return videoOutput;
    }

    private (string MediaInputs, string FilterComplex) BuildImagePipeline(
        List<GeneratedImage> segments,
        List<TimelineEntry> timeline,
        MontageSettings montage,
        string assPath,
        string? musicPath,
        double targetVideoDurationSec)
    {
        var fps = montage.OutputFps;
        var segmentDurationsSec = timeline.Select(t => t.DurationSec).ToList();

        var inputs = new StringBuilder();
        var filter = new StringBuilder();

        for (var i = 0; i < timeline.Count; i++)
        {
            var entry = timeline[i];
            var absImage = Path.GetFullPath(paths.ToAbsolutePath(segments[entry.SourceSegmentIndex].LocalFilePath!));
            inputs.Append(BuildLoopedImageInput(fps, absImage));
        }

        for (var i = 0; i < timeline.Count; i++)
        {
            var segment = segments[timeline[i].SourceSegmentIndex];
            var frames = Math.Max(1, (int)Math.Round(segmentDurationsSec[i] * fps));
            filter.AppendLine(
                $"[{i}:v]{montage.BuildImageKenBurnsChain(frames, segment.AnimationMode, segment.MotionSpeed, segment.MaxZoom)}[v{i}];");
        }

        AppendVideoMergeAndAudio(filter, timeline.Count, segmentDurationsSec, montage, assPath, musicPath, targetVideoDurationSec);

        return (inputs.ToString(), filter.ToString());
    }

    private (string MediaInputs, string FilterComplex) BuildVideoClipPipeline(
        List<GeneratedImage> segments,
        List<TimelineEntry> timeline,
        MontageSettings montage,
        string assPath,
        string? musicPath,
        double targetVideoDurationSec,
        IReadOnlyList<double> nativeFileDurationsSec)
    {
        var w = montage.Width;
        var h = montage.Height;
        var fps = montage.OutputFps;
        var scaleCrop = $"scale={w}:{h}:flags=lanczos:force_original_aspect_ratio=increase,crop={w}:{h},setsar=1";

        var segmentDurationsSec = timeline.Select(t => t.DurationSec).ToList();

        var inputs = new StringBuilder();
        var filter = new StringBuilder();

        for (var i = 0; i < timeline.Count; i++)
        {
            var entry = timeline[i];
            var absClip = Path.GetFullPath(paths.ToAbsolutePath(segments[entry.SourceSegmentIndex].LocalFilePath!));
            var nativeDuration = nativeFileDurationsSec[entry.SourceSegmentIndex];
            var needsExtendedPlay = entry.DurationSec > nativeDuration + 0.05;
            var shortfallAction = ResolveClipShortfallAction(montage, nativeDuration, entry.DurationSec);
            var useStretch = needsExtendedPlay && shortfallAction == "stretch";
            var useStreamLoop = needsExtendedPlay
                && shortfallAction is not "freeze_last"
                && !useStretch;

            if (useStreamLoop)
                inputs.Append($"-stream_loop -1 -i \"{absClip}\" ");
            else
                inputs.Append($"-i \"{absClip}\" ");
        }

        for (var i = 0; i < timeline.Count; i++)
        {
            var entry = timeline[i];
            var dur = entry.DurationSec.ToString(CultureInfo.InvariantCulture);
            var nativeDuration = nativeFileDurationsSec[entry.SourceSegmentIndex];
            var needsExtendedPlay = entry.DurationSec > nativeDuration + 0.05;
            var shortfallAction = ResolveClipShortfallAction(montage, nativeDuration, entry.DurationSec);

            if (needsExtendedPlay && shortfallAction == "stretch")
            {
                var stretchFactor = (entry.DurationSec / Math.Max(0.05, nativeDuration))
                    .ToString(CultureInfo.InvariantCulture);
                filter.AppendLine(
                    $"[{i}:v]setpts={stretchFactor}*PTS,trim=duration={dur},setpts=PTS-STARTPTS,fps={fps},{scaleCrop}[v{i}];");
            }
            else if (shortfallAction == "freeze_last" && needsExtendedPlay)
            {
                filter.AppendLine(
                    BuildMinterpolateExtensionFilter(i, nativeDuration, entry.DurationSec, fps, scaleCrop));
            }
            else
            {
                filter.AppendLine(
                    $"[{i}:v]trim=duration={dur},setpts=PTS-STARTPTS,fps={fps},{scaleCrop}[v{i}];");
            }
        }

        AppendVideoMergeAndAudio(filter, timeline.Count, segmentDurationsSec, montage, assPath, musicPath, targetVideoDurationSec);

        return (inputs.ToString(), filter.ToString());
    }

    private (string MediaInputs, string FilterComplex) BuildMixedSegmentPipeline(
        List<GeneratedImage> segments,
        List<TimelineEntry> timeline,
        MontageSettings montage,
        string assPath,
        string? musicPath,
        double targetVideoDurationSec,
        IReadOnlyList<double> nativeFileDurationsSec,
        bool stretchToFit = false)
    {
        var w = montage.Width;
        var h = montage.Height;
        var fps = montage.OutputFps;
        var scaleCrop = $"scale={w}:{h}:flags=lanczos:force_original_aspect_ratio=increase,crop={w}:{h},setsar=1";
        var segmentDurationsSec = timeline.Select(t => t.DurationSec).ToList();

        var inputs = new StringBuilder();
        var filter = new StringBuilder();

        for (var i = 0; i < timeline.Count; i++)
        {
            var entry = timeline[i];
            var segment = segments[entry.SourceSegmentIndex];
            var absPath = Path.GetFullPath(paths.ToAbsolutePath(segment.LocalFilePath!));

            if (MediaSegmentHelper.IsImageClip(segment.LocalFilePath))
                inputs.Append(BuildLoopedImageInput(fps, absPath));
            else
                inputs.Append($"-i \"{absPath}\" ");
        }

        for (var i = 0; i < timeline.Count; i++)
        {
            var entry = timeline[i];
            var segment = segments[entry.SourceSegmentIndex];
            var dur = entry.DurationSec.ToString(CultureInfo.InvariantCulture);

            if (MediaSegmentHelper.IsImageClip(segment.LocalFilePath))
            {
                var frames = Math.Max(1, (int)Math.Round(entry.DurationSec * fps));
                filter.AppendLine(
                    $"[{i}:v]{montage.BuildImageKenBurnsChain(frames, segment.AnimationMode, segment.MotionSpeed, segment.MaxZoom)}[v{i}];");
            }
            else
            {
                var nativeDuration = Math.Max(0.05, nativeFileDurationsSec[entry.SourceSegmentIndex]);
                if (stretchToFit && entry.DurationSec > nativeDuration + 0.05)
                {
                    var stretchFactor = (entry.DurationSec / nativeDuration).ToString(CultureInfo.InvariantCulture);
                    filter.AppendLine(
                        $"[{i}:v]setpts={stretchFactor}*PTS,trim=duration={dur},setpts=PTS-STARTPTS,fps={fps},{scaleCrop}[v{i}];");
                }
                else if (stretchToFit)
                {
                    filter.AppendLine(
                        $"[{i}:v]trim=duration={dur},setpts=PTS-STARTPTS,fps={fps},{scaleCrop}[v{i}];");
                }
                else
                {
                    var needsExtendedPlay = entry.DurationSec > nativeDuration + 0.05;
                    var shortfallAction = ResolveClipShortfallAction(montage, nativeDuration, entry.DurationSec);
                    if (needsExtendedPlay && shortfallAction == "stretch")
                    {
                        var stretchFactor = (entry.DurationSec / nativeDuration).ToString(CultureInfo.InvariantCulture);
                        filter.AppendLine(
                            $"[{i}:v]setpts={stretchFactor}*PTS,trim=duration={dur},setpts=PTS-STARTPTS,fps={fps},{scaleCrop}[v{i}];");
                    }
                    else if (shortfallAction == "freeze_last" && needsExtendedPlay)
                    {
                        var nativeDur = nativeDuration.ToString(CultureInfo.InvariantCulture);
                        var padDur = Math.Max(0, entry.DurationSec - nativeDuration).ToString(CultureInfo.InvariantCulture);
                        filter.AppendLine(
                            $"[{i}:v]trim=duration={nativeDur},setpts=PTS-STARTPTS,tpad=stop_mode=clone:stop_duration={padDur},fps={fps},{scaleCrop}[v{i}];");
                    }
                    else
                    {
                        filter.AppendLine(
                            $"[{i}:v]trim=duration={dur},setpts=PTS-STARTPTS,fps={fps},{scaleCrop}[v{i}];");
                    }
                }
            }
        }

        AppendVideoMergeAndAudio(filter, timeline.Count, segmentDurationsSec, montage, assPath, musicPath, targetVideoDurationSec);
        return (inputs.ToString(), filter.ToString());
    }

    private void AppendVideoMergeAndAudio(
        StringBuilder filter,
        int segmentCount,
        List<double> segmentDurationsSec,
        MontageSettings montage,
        string assPath,
        string? musicPath,
        double targetVideoDurationSec)
    {
        var xfadeTransition = "fade";
        var transDur = 0.5;
        string videoOutLabel;

        if (segmentCount == 1)
        {
            videoOutLabel = "v0";
        }
        else if (xfadeTransition is not null && transDur > 0)
        {
            var prev = "v0";
            var cumulative = segmentDurationsSec[0];
            for (var i = 1; i < segmentCount; i++)
            {
                var outLabel = i == segmentCount - 1 ? "vcats" : $"vx{i}";
                var offset = (cumulative - transDur).ToString(CultureInfo.InvariantCulture);
                var td = transDur.ToString(CultureInfo.InvariantCulture);
                filter.AppendLine($"[{prev}][v{i}]xfade=transition={xfadeTransition}:duration={td}:offset={offset}[{outLabel}];");
                prev = outLabel;
                cumulative += segmentDurationsSec[i] - transDur;
            }
            videoOutLabel = "vcats";
        }
        else
        {
            var concatInputs = string.Join("", Enumerable.Range(0, segmentCount).Select(i => $"[v{i}]"));
            filter.AppendLine($"{concatInputs}concat=n={segmentCount}:v=1:a=0[vcats];");
            videoOutLabel = "vcats";
        }

        var fps = montage.OutputFps;
        var videoChain = $"[{videoOutLabel}]";
        if (montage.SubtitlesEnabled && File.Exists(assPath))
        {
            var escapedAss = paths.EscapeForFfmpegAssFilter(assPath);
            videoChain += $"ass='{escapedAss}',";
        }
        filter.AppendLine($"{videoChain}fps={fps},format=yuv420p,setsar=1[vout];");

        var audioInputIndex = segmentCount;
        var targetAudioDur = targetVideoDurationSec.ToString(CultureInfo.InvariantCulture);
        filter.AppendLine(
            $"[{audioInputIndex}:a]aformat=sample_rates=44100:channel_layouts=stereo,volume={montage.VoiceVolume.ToString(CultureInfo.InvariantCulture)},apad=whole_dur={targetAudioDur}[narr];");

        if (musicPath is not null)
        {
            var musicInputIndex = audioInputIndex + 1;
            filter.AppendLine($"[{musicInputIndex}:a]aformat=sample_rates=44100:channel_layouts=stereo,volume={montage.MusicVolume.ToString(CultureInfo.InvariantCulture)}[music];");
            filter.AppendLine($"[music][narr]sidechaincompress=threshold=0.02:ratio=10:attack=200:release=1000:makeup=1[ducked];");
            filter.AppendLine($"[ducked]volume={montage.AudioDuckingLevel.ToString(CultureInfo.InvariantCulture)}[musicduck];");
            filter.AppendLine(
                $"[narr][musicduck]amix=inputs=2:duration=first:dropout_transition=2,aformat=sample_fmts=fltp:sample_rates=44100:channel_layouts=stereo[aout]");
        }
        else
        {
            filter.AppendLine(
                "[narr]aformat=sample_fmts=fltp:sample_rates=44100:channel_layouts=stereo[aout]");
        }
    }

    private async Task SyncProjectTimingsToAudioAsync(
        VideoProject project, double audioDurationSec, MontageSettings montage, bool nativeClipDurations, CancellationToken ct)
    {
        var images = project.GeneratedImages.OrderBy(i => i.SortOrder).ToList();
        IReadOnlyList<double>? probedNativeDurations = null;
        if (nativeClipDurations || images.All(i => MediaSegmentHelper.IsVideoClip(i.LocalFilePath)))
        {
            var ffmpeg = FfmpegLocator.Resolve(montage.FfmpegPath);
            probedNativeDurations = await ProbeSegmentFileDurationsAsync(ffmpeg, images, ct);
        }

        var timeline = MediaTimelineHelper.BuildTimeline(
            images, montage, audioDurationSec, nativeClipDurations, probedNativeDurations);
        var targetDuration = MediaTimelineHelper.ResolveOutputDurationSec(montage, audioDurationSec, timeline);
        timeline = MediaTimelineHelper.CompensateForTransitionOverlap(timeline, montage, targetDuration);
        MediaTimelineHelper.AssignImageTimingsFromTimeline(images, timeline);

        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task GenerateAssSubtitlesAsync(
        VideoProject project, string assPath, MontageSettings montage,
        double audioDurationSec, double videoDurationSec, CancellationToken ct)
    {
        var words = string.IsNullOrWhiteSpace(project.WordTimestampsJson)
            ? []
            : JsonSerializer.Deserialize<List<WordTimestampDto>>(project.WordTimestampsJson, JsonOptions) ?? [];

        words = MediaTimelineHelper.ApplySubtitleSync(words, montage, audioDurationSec, videoDurationSec);

        var sb = new StringBuilder();
        sb.AppendLine("[Script Info]");
        sb.AppendLine("Title: Content Empire Subtitles");
        sb.AppendLine("ScriptType: v4.00+");
        sb.AppendLine($"PlayResX: {montage.Width}");
        sb.AppendLine($"PlayResY: {montage.Height}");
        sb.AppendLine();
        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        var primaryColor = AssColorHelper.Normalize(montage.PrimaryColor, "&H00FFFFFF");
        var highlightColor = AssColorHelper.Normalize(montage.HighlightColor, "&H0000FFFF");
        var backColor = AssColorHelper.Normalize(montage.BackColor, "&H80000000");
        sb.AppendLine($"Style: Default,{montage.FontName},{montage.FontSize},{primaryColor},{highlightColor},&H00000000,{backColor},-1,0,0,0,100,100,0,0,1,{montage.SubtitleOutline},1,{montage.SubtitleAlignment},40,40,120,1");
        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        foreach (var word in words)
        {
            var start = FormatAssTime(word.StartMs);
            var end = FormatAssTime(word.EndMs);
            sb.AppendLine($"Dialogue: 0,{start},{end},Default,,0,0,0,,{word.Word}");
        }

        await WriteTextFileWithRetryAsync(assPath, sb.ToString(), ct);
    }

    private static async Task WriteTextFileWithRetryAsync(string targetPath, string content, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = targetPath + $".{Guid.NewGuid():N}.tmp";
        const int maxAttempts = 8;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await File.WriteAllTextAsync(tempPath, content, ct);
                if (File.Exists(targetPath))
                    File.Delete(targetPath);
                File.Move(tempPath, targetPath);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(150 * attempt), ct);
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }

        throw new IOException(
            $"تعذّر كتابة الملف '{targetPath}'. أغلق أي نافذة تفتحه (مثل Notepad) أو انتظر انتهاء FFmpeg ثم أعد المحاولة.");
    }

    private static string FormatAssTime(double ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalHours:D1}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}";
    }

    private async Task<List<double>> ProbeSegmentFileDurationsAsync(
        string ffmpeg, IList<GeneratedImage> segments, CancellationToken ct)
    {
        var durations = new List<double>(segments.Count);
        foreach (var segment in segments)
        {
            var path = Path.GetFullPath(paths.ToAbsolutePath(segment.LocalFilePath!));
            durations.Add(await MediaDurationHelper.ProbeDurationSecondsAsync(ffmpeg, path, ct));
        }

        return durations;
    }

    private static string BuildMinterpolateExtensionFilter(
        int inputIndex, double nativeDuration, double targetDuration, int fps, string scaleCrop)
    {
        var nativeDur = Math.Max(0.05, nativeDuration).ToString(CultureInfo.InvariantCulture);
        var dur = targetDuration.ToString(CultureInfo.InvariantCulture);
        var stretchFactor = (targetDuration / Math.Max(0.05, nativeDuration))
            .ToString(CultureInfo.InvariantCulture);
        return
            $"[{inputIndex}:v]trim=duration={nativeDur},setpts=PTS-STARTPTS,setpts={stretchFactor}*PTS," +
            $"minterpolate=mi_mode=mci:mc_mode=aobmc:vsbmc=1:fps={fps},trim=duration={dur}," +
            $"setpts=PTS-STARTPTS,fps={fps},{scaleCrop}[v{inputIndex}];";
    }

    private static string ResolveClipShortfallAction(
        MontageSettings montage, double nativeDuration, double targetDuration)
    {
        if (targetDuration <= nativeDuration + 0.05)
            return "trim";

        if (montage.MediaShortfallAction == "freeze_last")
            return "freeze_last";

        if (montage.MediaShortfallAction == "stretch")
        {
            var factor = targetDuration / Math.Max(0.05, nativeDuration);
            return factor <= montage.WanMaxStretchFactor ? "stretch" : "freeze_last";
        }

        return montage.MediaShortfallAction;
    }

    private static string BuildLoopedImageInput(int fps, string absolutePath) =>
        $"-loop 1 -framerate {fps.ToString(CultureInfo.InvariantCulture)} -i \"{absolutePath}\" ";

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup of intermediate render file.
        }
    }

    private static async Task RunFfmpegAsync(string ffmpeg, string arguments, string workingDirectory, CancellationToken ct)
    {
        if (!File.Exists(ffmpeg) && ffmpeg is "ffmpeg" or "ffmpeg.exe")
        {
            throw new InvalidOperationException(
                "FFmpeg not found. Install it: winget install Gyan.FFmpeg — then restart the server, " +
                "or set the full path in Dashboard → Settings → FFmpeg Executable Path.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"FFmpeg not found at '{ffmpeg}'");

        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg failed (exit {process.ExitCode}): {stderr}");
    }
}

internal static class AssColorHelper
{
    public static string Normalize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var trimmed = value.Trim();
        if (trimmed.EndsWith('&') && !trimmed.StartsWith('&'))
            trimmed = trimmed.TrimEnd('&');

        if (!trimmed.StartsWith('&'))
            trimmed = trimmed.StartsWith("H", StringComparison.OrdinalIgnoreCase) ? "&" + trimmed : "&H" + trimmed;

        return trimmed.StartsWith("&H", StringComparison.OrdinalIgnoreCase) ? trimmed : fallback;
    }
}

internal static class FfmpegLocator
{
    public static string Resolve(string configuredPath)
    {
        if (File.Exists(configuredPath))
            return Path.GetFullPath(configuredPath);

        if (configuredPath.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase)
            || configuredPath.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
        {
            var fromPath = FindOnPath("ffmpeg.exe");
            if (fromPath is not null) return fromPath;

            var wingetRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(wingetRoot))
            {
                var match = Directory.GetFiles(wingetRoot, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (match is not null) return match;
            }
        }

        return configuredPath;
    }

    private static string? FindOnPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
