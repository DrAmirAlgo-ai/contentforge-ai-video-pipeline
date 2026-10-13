using System.Text.Json;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Entities;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContentEmpireSystem.Infrastructure.Services;

public class VoiceCodeService(
    ApplicationDbContext db,
    ISystemSettingsService settings,
    IOutputPathService paths,
    ICurrentUserAccessor currentUser) : IVoiceCodeService
{
    private static readonly JsonSerializerOptions JsonOptions = LlmJsonParser.DefaultOptions;
    private static readonly HashSet<string> AllowedImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public async Task<VideoProjectDto> ConfirmIdeaAsync(ConfirmManualIdeaRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Idea))
            throw new InvalidOperationException("أدخل نص الفكرة أولاً.");

        var project = await GetOrCreateProjectAsync(request.ProjectId, ct);
        project.CurrentIdea = request.Idea.Trim();
        project.ConfirmedIdea = project.CurrentIdea;
        project.Status = VideoProjectStatus.IdeaConfirmed;
        project.AiProviderUsed = "VoiceCode";
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return VideoProjectMapper.Map(await LoadProjectAsync(project.Id, ct));
    }

    public async Task<VideoProjectDto> SaveScriptAsync(VoiceCodeScriptRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ScriptText))
            throw new InvalidOperationException("أدخل نص Voice Code أولاً.");

        var text = request.ScriptText.Trim();
        if (!VoiceCodeParser.ContainsVoiceCodeMarkers(text))
            throw new InvalidOperationException("السكريبت يجب أن يحتوي على مقاطع [VC:1] … [VC:n].");

        var voiceSegments = VoiceCodeParser.Parse(text);
        if (voiceSegments.Count < 3)
            throw new InvalidOperationException("Voice Code يحتاج 3 مقاطع على الأقل.");

        var project = await db.VideoProjects
            .Include(p => p.GeneratedImages)
            .FirstAsync(p => p.Id == request.ProjectId, ct);

        if (string.IsNullOrWhiteSpace(project.ConfirmedIdea) && string.IsNullOrWhiteSpace(project.CurrentIdea))
            throw new InvalidOperationException("أكّد الفكرة أو ولّدها قبل حفظ Voice Code.");

        var script = BuildScriptFromVoiceCode(voiceSegments, project.ConfirmedIdea ?? project.CurrentIdea);
        script.VoiceCodeRawText = text;
        VoiceCodeParser.SyncMediaFlags(script, project.GeneratedImages);

        var rules = await settings.GetProductionRulesAsync(ct);
        var plan = ProductionPlanResolver.BuildWordCountPlan(
            LongScriptNarrationPlanner.CountWords(script.FullNarrationText),
            project.ProductionWorkflowPath);
        ProductionPlanResolver.FinalizePlan(plan, script, rules.MaxVideoDurationSec);
        script.ProductionPlan = plan;
        ProductionPlanResolver.ApplyToProject(project, plan);

        project.ScriptJson = JsonSerializer.Serialize(script, JsonOptions);
        project.ImageProviderUsed = "VoiceCode";
        project.Status = VideoProjectStatus.ScriptGenerated;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return VideoProjectMapper.Map(await LoadProjectAsync(project.Id, ct));
    }

    public async Task<VideoProjectDto> UploadSegmentMediaAsync(
        Guid projectId, int sortOrder, int partIndex, Stream file, string fileName, CancellationToken ct = default)
    {
        if (sortOrder < 1 || sortOrder > VoiceCodeTimingHelper.MaxVoiceSegments)
            throw new InvalidOperationException($"رقم المقطع يجب أن يكون بين 1 و {VoiceCodeTimingHelper.MaxVoiceSegments}.");

        if (partIndex is < 0 or >= VoiceCodeTimingHelper.MaxVisualPartsPerSegment)
            throw new InvalidOperationException("رقم الفيجوال يجب أن يكون 0 (الأول) أو 1 (الثاني).");

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var isVideo = MediaSegmentHelper.VideoExtensions.Contains(ext);
        var isImage = AllowedImageExtensions.Contains(ext);
        if (!isVideo && !isImage)
            throw new InvalidOperationException("صيغة غير مدعومة. استخدم JPG/PNG/WEBP أو MP4/MOV/WEBM/MKV.");

        var project = await db.VideoProjects
            .Include(p => p.GeneratedImages)
            .FirstAsync(p => p.Id == projectId, ct);

        var script = DeserializeScript(project.ScriptJson)
            ?? throw new InvalidOperationException("احفظ سكريبت Voice Code أولاً.");

        if (!script.IsVoiceCode)
            throw new InvalidOperationException("هذا المشروع لا يستخدم Voice Code.");

        var voiceSeg = script.VoiceSegments.FirstOrDefault(v => v.SortOrder == sortOrder)
            ?? throw new InvalidOperationException($"المقطع [VC:{sortOrder}] غير موجود في السكريبت.");

        var configuredOutput = await settings.GetOrDefaultAsync(SettingKeys.OutputDirectory, "output", ct);
        var mediaDir = Path.Combine(paths.GetProjectDirectory(configuredOutput, projectId), "voicecode");
        Directory.CreateDirectory(mediaDir);

        if (file.CanSeek)
            file.Position = 0;

        var localPath = Path.Combine(mediaDir, $"seg_{sortOrder:D2}_p{partIndex}{ext}");
        await using (var fs = File.Create(localPath))
            await file.CopyToAsync(fs, ct);

        if (!File.Exists(localPath) || new FileInfo(localPath).Length == 0)
            throw new InvalidOperationException($"فشل حفظ ملف المقطع {sortOrder} (فيجوال {partIndex + 1}).");

        var absolutePath = Path.GetFullPath(localPath);
        var existing = project.GeneratedImages.FirstOrDefault(i => i.SortOrder == sortOrder && i.VisualPartIndex == partIndex);
        if (existing is not null)
        {
            existing.LocalFilePath = absolutePath;
            existing.Prompt = $"Voice Code — {voiceSeg.Label ?? $"مقطع {sortOrder}"} · فيجوال {partIndex + 1}";
            existing.ImageUrl = null;
        }
        else
        {
            db.GeneratedImages.Add(new GeneratedImage
            {
                VideoProjectId = projectId,
                Prompt = $"Voice Code — {voiceSeg.Label ?? $"مقطع {sortOrder}"} · فيجوال {partIndex + 1}",
                LocalFilePath = absolutePath,
                SortOrder = sortOrder,
                VisualPartIndex = partIndex
            });
        }

        project.ImageProviderUsed = "VoiceCode";
        project.VideoFilePath = null;
        if (project.Status < VideoProjectStatus.ScriptGenerated)
            project.Status = VideoProjectStatus.ScriptGenerated;

        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrWhiteSpace(project.AudioFilePath))
        {
            var ffmpeg = FfmpegLocator.Resolve(
                await settings.GetOrDefaultAsync(SettingKeys.FfmpegPath, "ffmpeg", ct));
            var audioSec = await MediaDurationHelper.ProbeDurationSecondsAsync(
                ffmpeg, paths.ToAbsolutePath(project.AudioFilePath), ct);
            var images = VoiceCodeTimingHelper.OrderForPlayback(project.GeneratedImages);
            VoiceCodeTimingHelper.ApplyTimings(images, script.VoiceSegments, audioSec);
            project.WordTimestampsJson = await BuildVoiceCodeTimestampsAsync(script, audioSec);
        }

        VoiceCodeParser.SyncMediaFlags(script, project.GeneratedImages);
        project.ScriptJson = JsonSerializer.Serialize(script, JsonOptions);
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return VideoProjectMapper.Map(await LoadProjectAsync(projectId, ct));
    }

    private async Task<VideoProject> GetOrCreateProjectAsync(Guid? projectId, CancellationToken ct)
    {
        if (projectId.HasValue)
            return await db.VideoProjects
                .Include(p => p.GeneratedImages)
                .FirstAsync(p => p.Id == projectId.Value, ct);

        var project = new VideoProject
        {
            OwnerUserId = currentUser.RequireUserId(),
        };

        db.VideoProjects.Add(project);
        await db.SaveChangesAsync(ct);
        return project;
    }

    private static ScriptOutputDto BuildScriptFromVoiceCode(
        List<VoiceSegmentDto> voiceSegments, string? idea)
    {
        var narration = VoiceCodeParser.BuildFullNarration(voiceSegments);
        var title = idea?.Length > 80 ? idea[..80] + "…" : idea ?? "Voice Code";
        return new ScriptOutputDto
        {
            ViewerBenefit = "Voice Code — مقاطع صوتية متزامنة",
            Seo = new SeoMetadataDto { Title = title, Description = narration.Length > 200 ? narration[..200] : narration },
            PrimaryHook = voiceSegments[0].Text.Length > 120 ? voiceSegments[0].Text[..120] : voiceSegments[0].Text,
            ExtendedHook = narration,
            Body = new ScriptBodyDto { PartOneProblem = "", PartTwoSolution = narration },
            ConclusionAndCta = "",
            FullNarrationText = narration,
            VoiceCodeRawText = VoiceCodeParser.BuildRawText(voiceSegments),
            VoiceSegments = voiceSegments,
            ImageSuggestions = voiceSegments.Select(v => new ImageSuggestionDto
            {
                SortOrder = v.SortOrder,
                Prompt = v.Label ?? $"Segment {v.SortOrder}",
                SceneDescription = v.Text.Length > 100 ? v.Text[..100] : v.Text
            }).ToList()
        };
    }

    private static ScriptOutputDto? DeserializeScript(string? scriptJson) =>
        ScriptJsonReader.TryParse(scriptJson);

    private static Task<string?> BuildVoiceCodeTimestampsAsync(ScriptOutputDto script, double audioSec)
    {
        if (script.VoiceSegments.Count == 0)
            return Task.FromResult<string?>(null);

        var wordCounts = script.VoiceSegments.OrderBy(v => v.SortOrder).Select(v =>
            v.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length).ToList();
        var totalWords = wordCounts.Sum();
        if (totalWords == 0)
            return Task.FromResult<string?>(null);

        var timestamps = new List<WordTimestampDto>();
        var cursorMs = 0d;
        var ordered = script.VoiceSegments.OrderBy(v => v.SortOrder).ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            var words = ordered[i].Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var segmentDurationMs = audioSec * 1000d * (wordCounts[i] / (double)totalWords);
            var msPerWord = words.Length > 0 ? segmentDurationMs / words.Length : segmentDurationMs;

            for (var w = 0; w < words.Length; w++)
            {
                timestamps.Add(new WordTimestampDto
                {
                    Word = words[w],
                    StartMs = cursorMs + w * msPerWord,
                    EndMs = cursorMs + (w + 1) * msPerWord
                });
            }

            cursorMs += segmentDurationMs;
        }

        return Task.FromResult<string?>(JsonSerializer.Serialize(timestamps));
    }

    private async Task<VideoProject> LoadProjectAsync(Guid id, CancellationToken ct) =>
        await db.VideoProjects
            .Include(p => p.GeneratedImages)
            .AsNoTracking()
            .FirstAsync(p => p.Id == id, ct);
}
