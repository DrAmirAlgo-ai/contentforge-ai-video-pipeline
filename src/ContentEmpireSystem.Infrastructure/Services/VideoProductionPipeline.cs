using System.Text.Json;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Entities;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentEmpireSystem.Infrastructure.Services;

public class VideoProductionPipeline(
    ApplicationDbContext db,
    ISystemSettingsService settings,
    IOutputPathService paths,
    ITextToSpeechService ttsService,
    IFfmpegVideoService ffmpegService,
    IWorkloadGate workloadGate,
    SystemActivityService activity,
    IOperationFileLogger operationLog,
    ILogger<VideoProductionPipeline> logger) : IVideoProductionPipeline
{
    public async Task StartProductionAsync(Guid projectId, CancellationToken ct = default)
    {
        const string op = "تركيب FFmpeg";
        operationLog.LogStart(projectId, op);
        activity.Start(projectId, "تركيب الفيديو", "FFmpeg + TTS");
        await using var gate = await workloadGate.AcquireAsync(WorkloadSlot.Production, projectId, ct);

        try
        {
            var project = await LoadProjectAsync(projectId, ct);

            if (string.IsNullOrWhiteSpace(project.ScriptJson))
                throw new InvalidOperationException("لا يوجد سكريبت. ولّد السكريبت أو الصقه يدوياً قبل الإنتاج.");

            project.Status = VideoProjectStatus.Processing;
            project.RejectionReason = null;
            project.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var script = ScriptJsonReader.TryParse(project.ScriptJson)
                ?? throw new InvalidOperationException("بيانات السكريبت غير صالحة. أعد توليد السكريبت.");
            var narration = ResolveNarration(script);
            if (!HasExistingAudioFile(project))
            {
                var rules = ProductionRulesDto.FromSettings(await settings.GetAllAsDictionaryAsync(ct));
                var sceneSecRaw = await settings.GetOrDefaultAsync(SettingKeys.ImageDurationSec, "5", ct);
                var sceneSec = double.TryParse(sceneSecRaw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 5;
                var maxWords = NarrationWordLimiter.ResolveMaxWords(script, rules.WordsPerSecond, sceneSec);
                narration = NarrationWordLimiter.Enforce(narration, maxWords);
                await ttsService.GenerateSpeechAsync(projectId, narration, ct);
            }

            project = await LoadProjectAsync(projectId, ct);

            if (project.GeneratedImages.Count == 0)
                throw new InvalidOperationException("لا توجد صور أو مقاطع فيديو. ارفع 3–10 صور أو مقاطع فيديو يدوياً، أو أكمل Voice Code.");

            ValidateVoiceCodeProject(project);
            await ValidateMaxAudioDurationAsync(project, ct);

            await ApplyAutoMotionIfEnabledAsync(project, ct);
            await AssignImageTimingsAsync(project, ct);

            var videoPath = await ffmpegService.RenderVideoAsync(projectId, ct);

            project.VideoFilePath = videoPath;
            operationLog.LogFileExchange(projectId, op, "scenes+audio", videoPath, "final render");
            project.Status = VideoProjectStatus.Published;
            project.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            operationLog.LogComplete(projectId, op, Path.GetFileName(videoPath));
            activity.Complete(projectId, "فيديو نهائي جاهز");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Production failed for project {ProjectId}", projectId);
            operationLog.LogFail(projectId, op, ex.Message, ex);
            var reason = string.IsNullOrWhiteSpace(ex.Message)
                ? ex.GetType().Name
                : ex.Message;
            var failed = await db.VideoProjects.FirstAsync(p => p.Id == projectId, ct);
            failed.Status = VideoProjectStatus.Failed;
            failed.RejectionReason = reason.Length > 2000 ? reason[..2000] : reason;
            failed.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            activity.Fail(projectId, reason);
        }
    }

    private async Task<VideoProject> LoadProjectAsync(Guid projectId, CancellationToken ct) =>
        await db.VideoProjects
            .Include(p => p.GeneratedImages)
            .FirstAsync(p => p.Id == projectId, ct);

    private async Task AssignImageTimingsAsync(VideoProject project, CancellationToken ct)
    {
        var images = project.GeneratedImages.OrderBy(i => i.SortOrder).ToList();
        if (images.Count == 0) return;

        var montage = await MontageSettings.LoadAsync(settings, ct);
        var scriptForSync = ScriptJsonReader.TryParse(project.ScriptJson);
        var isVoiceCode = string.Equals(project.ImageProviderUsed, "VoiceCode", StringComparison.OrdinalIgnoreCase)
            || scriptForSync?.IsVoiceCode == true;
        var isManualVideo = string.Equals(project.ImageProviderUsed, "ManualVideo", StringComparison.OrdinalIgnoreCase);

        double audioDurationSec = 0;
        if (!string.IsNullOrWhiteSpace(project.AudioFilePath))
        {
            var ffmpeg = FfmpegLocator.Resolve(montage.FfmpegPath);
            var audioPath = paths.ToAbsolutePath(project.AudioFilePath);
            audioDurationSec = await MediaDurationHelper.ProbeDurationSecondsAsync(ffmpeg, audioPath, ct);
        }

        if (isVoiceCode && audioDurationSec > 0 && scriptForSync?.IsVoiceCode == true)
        {
            VoiceCodeTimingHelper.ApplyTimings(
                VoiceCodeTimingHelper.OrderForPlayback(images),
                scriptForSync.VoiceSegments,
                audioDurationSec);
            project.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        List<double>? probedNativeDurations = null;
        if (isManualVideo || (!isVoiceCode && images.All(i => MediaSegmentHelper.IsVideoClip(i.LocalFilePath))))
        {
            var ffmpeg = FfmpegLocator.Resolve(montage.FfmpegPath);
            probedNativeDurations = new List<double>(images.Count);
            foreach (var image in images)
            {
                var path = paths.ToAbsolutePath(image.LocalFilePath!);
                probedNativeDurations.Add(await MediaDurationHelper.ProbeDurationSecondsAsync(ffmpeg, path, ct));
            }
        }

        var timeline = MediaTimelineHelper.BuildTimeline(
            images,
            montage,
            audioDurationSec,
            nativeClipDurations: isManualVideo || (!isVoiceCode && probedNativeDurations is not null),
            probedNativeDurations);

        if (timeline.Count > 0)
        {
            var targetDuration = MediaTimelineHelper.ResolveOutputDurationSec(montage, audioDurationSec, timeline);
            timeline = MediaTimelineHelper.CompensateForTransitionOverlap(timeline, montage, targetDuration);
            MediaTimelineHelper.AssignImageTimingsFromTimeline(images, timeline);
        }
        else
        {
            var fallbackDurationMs = audioDurationSec > 0
                ? audioDurationSec * 1000d
                : images.Count * montage.ImageDurationSec * 1000d;
            MediaDurationHelper.DistributeEqualImageTimings(images, fallbackDurationMs);
        }

        if (!string.IsNullOrWhiteSpace(project.WordTimestampsJson))
        {
            var words = JsonSerializer.Deserialize<List<WordTimestampDto>>(project.WordTimestampsJson, LlmJsonParser.DefaultOptions)!;
            var videoDurationSec = timeline.Count > 0
                ? MediaTimelineHelper.ResolveOutputDurationSec(montage, audioDurationSec, timeline)
                : audioDurationSec;
            var synced = MediaTimelineHelper.ApplySubtitleSync(words, montage, audioDurationSec, videoDurationSec);
            project.WordTimestampsJson = JsonSerializer.Serialize(synced);
        }

        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyAutoMotionIfEnabledAsync(VideoProject project, CancellationToken ct)
    {
        var mode = await settings.GetOrDefaultAsync(SettingKeys.ImageMotionAssignment, "auto", ct);
        if (!ImageMotionAutoAssigner.IsAutoMode(mode))
            return;

        ImageMotionAutoAssigner.Apply(project.GeneratedImages);
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private bool HasExistingAudioFile(VideoProject project)
    {
        if (string.IsNullOrWhiteSpace(project.AudioFilePath))
            return false;

        try
        {
            var full = paths.ToAbsolutePath(project.AudioFilePath);
            return File.Exists(full) && new FileInfo(full).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveNarration(ScriptOutputDto script) =>
        script.FullNarrationText?.Trim() ?? "";

    private async Task ValidateMaxAudioDurationAsync(VideoProject project, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(project.AudioFilePath))
            return;

        var configuredMax = await settings.GetOrDefaultAsync(
            SettingKeys.MaxVideoDurationSec, ScriptGenerationHelper.MaxDurationSec.ToString(), ct);
        var maxSec = ScriptGenerationHelper.ResolveMaxDurationSec(configuredMax);

        var ffmpeg = FfmpegLocator.Resolve(
            await settings.GetOrDefaultAsync(SettingKeys.FfmpegPath, "ffmpeg", ct));
        var audioSec = await MediaDurationHelper.ProbeDurationSecondsAsync(
            ffmpeg, paths.ToAbsolutePath(project.AudioFilePath), ct);

        if (ProductionDurationValidator.ExceedsMaxDuration(audioSec, maxSec))
            throw new InvalidOperationException(
                ProductionDurationValidator.FormatExceedsError(audioSec, maxSec));
    }

    private static void ValidateVoiceCodeProject(VideoProject project)
    {
        var script = ScriptJsonReader.TryParse(project.ScriptJson);
        if (script?.IsVoiceCode != true)
            return;

        var missing = script.VoiceSegments
            .Where(v => !project.GeneratedImages.Any(i =>
                i.SortOrder == v.SortOrder
                && !string.IsNullOrWhiteSpace(i.LocalFilePath)))
            .Select(v => v.SortOrder)
            .ToList();

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Voice Code: ارفع صورة أو فيديو لكل مقطع. المقاطع الناقصة: {string.Join(", ", missing)}");
    }
}
