using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Entities;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Data;
using ContentEmpireSystem.Infrastructure.Services.Ai;
using Microsoft.EntityFrameworkCore;

namespace ContentEmpireSystem.Infrastructure.Services;
public class VideoProjectMapper
{
    public static async Task<VideoProjectDto> MapWithWorkflowAsync(
        VideoProject project, IProjectWorkflowBuilder workflowBuilder, CancellationToken ct = default)
        => await MapWithWorkflowAsync(project, workflowBuilder, paths: null, ct);

    public static async Task<VideoProjectDto> MapWithWorkflowAsync(
        VideoProject project,
        IProjectWorkflowBuilder workflowBuilder,
        IOutputPathService? paths,
        CancellationToken ct)
    {
        var dto = Map(project, paths);
        dto.Workflow = await workflowBuilder.BuildAsync(project, ct);
        return dto;
    }

    public static VideoProjectDto Map(VideoProject project, IOutputPathService? paths = null)
    {
        ScriptOutputDto? script = null;
        if (!string.IsNullOrWhiteSpace(project.ScriptJson))
        {
            script = ScriptJsonReader.TryParse(project.ScriptJson);
            if (script is not null && script.ProductionPlan is null)
                script.ProductionPlan = ProductionPlanResolver.Resolve(project, script);
            if (script?.IsVoiceCode == true)
                VoiceCodeParser.SyncMediaFlags(script, project.GeneratedImages);
        }

        return new VideoProjectDto
        {
            Id = project.Id,
            Status = project.Status,
            RejectionReason = project.RejectionReason,
            CurrentIdea = project.CurrentIdea,
            ConfirmedIdea = project.ConfirmedIdea,
            Script = script,
            VideoFilePath = project.VideoFilePath,
            AiProviderUsed = project.AiProviderUsed,
            TtsProviderUsed = project.TtsProviderUsed,
            ImageProviderUsed = project.ImageProviderUsed,
            VideoProviderUsed = project.VideoProviderUsed,
            ProductionWorkflowPath = project.ProductionWorkflowPath,
            HasManualAudio = project.TtsProviderUsed == "Manual",
            HasAudio = paths is not null
                ? ProjectMediaPaths.HasAudioFile(project, paths)
                : !string.IsNullOrWhiteSpace(project.AudioFilePath),
            HasManualImages = project.ImageProviderUsed == "Manual",
            HasManualVideos = project.ImageProviderUsed == "ManualVideo",
            HasVoiceCode = string.Equals(project.ImageProviderUsed, "VoiceCode", StringComparison.OrdinalIgnoreCase)
                           || (script?.IsVoiceCode ?? false),
            TargetWordCount = project.TargetWordCount > 0
                ? project.TargetWordCount
                : ScriptGenerationHelper.EstimateWordCount(project.TargetDurationSec),
            TargetDurationSec = ResolveTargetDurationSec(project, script),
            ImageSegmentCount = ResolveImageSegmentCount(project, script),
            VideoSegmentCount = ResolveVideoSegmentCount(project, script),
            Images = project.GeneratedImages.OrderBy(i => i.SortOrder).Select(i => new GeneratedImageDto
            {
                Id = i.Id,
                Prompt = i.Prompt,
                ScenePrompt = i.ScenePrompt,
                ImageUrl = i.ImageUrl,
                LocalFilePath = i.LocalFilePath,
                SortOrder = i.SortOrder,
                VisualPartIndex = i.VisualPartIndex,
                IsVideoClip = MediaSegmentHelper.IsVideoClip(i.LocalFilePath),
                DurationSec = Math.Max(0, i.EndTimeMs - i.StartTimeMs) / 1000.0,
                AnimationMode = ImageMotionHelper.NormalizeMode(i.AnimationMode),
                MotionSpeed = ImageMotionHelper.NormalizeMotionSpeed(i.MotionSpeed),
                MaxZoom = ImageMotionHelper.NormalizeMaxZoom(i.MaxZoom)
            }).ToList(),
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt
        };
    }

    private static int ResolveTargetDurationSec(VideoProject project, ScriptOutputDto? script)
    {
        if (script?.ProductionPlan?.EstimatedDurationSec > 0)
            return script.ProductionPlan.EstimatedDurationSec;
        if (project.TargetDurationSec > 0)
            return project.TargetDurationSec;
        if (script?.ProductionPlan?.TargetWordCount > 0)
            return ScriptGenerationHelper.EstimateDurationSec(script.ProductionPlan.TargetWordCount);
        return ScriptGenerationHelper.EstimateDurationSec(
            project.TargetWordCount > 0 ? project.TargetWordCount : ScriptGenerationHelper.DefaultWordCount);
    }

    private static int ResolveImageSegmentCount(VideoProject project, ScriptOutputDto? script)
    {
        if (script?.ProductionPlan?.ImageSegmentCount > 0)
            return script.ProductionPlan.ImageSegmentCount;
        if (script?.ImageSuggestions.Count > 0)
            return script.ImageSuggestions.Count;
        if (project.ImageSegmentCount > 0)
            return project.ImageSegmentCount;
        return 0;
    }

    private static int ResolveVideoSegmentCount(VideoProject project, ScriptOutputDto? script)
    {
        if (script?.ProductionPlan?.VideoSegmentCount > 0)
            return script.ProductionPlan.VideoSegmentCount;
        if (script?.IsVoiceCode == true && script.VoiceSegments.Count > 0)
            return script.VoiceSegments.Count;
        if (script?.VideoSuggestions.Count > 0)
            return script.VideoSuggestions.Count;
        if (project.VideoSegmentCount > 0)
            return project.VideoSegmentCount;
        return 0;
    }
}
