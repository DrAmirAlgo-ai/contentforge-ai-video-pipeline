using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Entities;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ContentEmpireSystem.Infrastructure.Services;

public sealed class ProjectWorkflowBuilder(
    ISystemSettingsService settings,
    IOutputPathService paths,
    IProjectOperationCoordinator coordinator,
    ILogger<ProjectWorkflowBuilder> logger) : IProjectWorkflowBuilder
{
    public async Task<ProjectWorkflowStateDto> BuildAsync(VideoProject project, CancellationToken ct = default)
    {
        var profile = await DeploymentProfileSource.ResolveAsync(settings, ct);
        var videoSupported = DeploymentProfileResolver.SupportsVideoGeneration(profile);
        var rules = await settings.GetProductionRulesAsync(ct);
        // When the user requires Wan animation, the pipeline must NOT skip straight to
        // produce with static images — it stops at the Wan step so "Animate Images" unlocks.
        var requireWan = string.Equals(
            await settings.GetOrDefaultAsync(SettingKeys.RequireWanAnimation, "true", ct),
            "true", StringComparison.OrdinalIgnoreCase);
        return Build(project, videoSupported, rules, allowStaticProduce: !requireWan);
    }

    public ProjectWorkflowStateDto Build(VideoProject project, bool videoGenerationSupported)
        => Build(project, videoGenerationSupported, ProductionRulesDto.Defaults);

    public ProjectWorkflowStateDto Build(VideoProject project, bool videoGenerationSupported, ProductionRulesDto rules)
        => Build(project, videoGenerationSupported, rules, allowStaticProduce: true);

    public ProjectWorkflowStateDto Build(
        VideoProject project, bool videoGenerationSupported, ProductionRulesDto rules, bool allowStaticProduce)
    {
        ScriptOutputDto? script = null;
        if (!string.IsNullOrWhiteSpace(project.ScriptJson))
        {
            script = ScriptJsonReader.TryParse(project.ScriptJson);
            if (script is null)
                logger.LogWarning("تعذّر تحليل ScriptJson للمشروع {ProjectId}", project.Id);
        }

        var images = project.GeneratedImages.OrderBy(i => i.SortOrder).ToList();
        var staticCount = images.Count(i => !ImageMotionHelper.IsVideoClip(i.LocalFilePath));
        var clipCount = images.Count(i => ImageMotionHelper.IsVideoClip(i.LocalFilePath));
        var hasAudio = ProjectMediaPaths.HasAudioFile(project, paths);

        var isVoiceCode = string.Equals(project.ImageProviderUsed, "VoiceCode", StringComparison.OrdinalIgnoreCase)
            || script?.IsVoiceCode == true;

        if (isVoiceCode && script is not null)
            VoiceCodeParser.SyncMediaFlags(script, images);

        var voiceCodeMediaCount = script is not null && isVoiceCode
            ? VoiceCodeParser.CountSegmentsWithMedia(script, images)
            : 0;

        coordinator.IsBusy(project.Id, out var activeOp);

        var input = new ProjectWorkflowInput
        {
            Status = project.Status,
            ProductionWorkflowPath = project.ProductionWorkflowPath,
            CurrentIdea = project.CurrentIdea,
            ConfirmedIdea = project.ConfirmedIdea,
            Script = script,
            TargetWordCount = project.TargetWordCount,
            ImageSegmentCount = project.ImageSegmentCount,
            VideoSegmentCount = project.VideoSegmentCount,
            StaticImageCount = staticCount,
            VideoClipCount = clipCount,
            SegmentCount = isVoiceCode ? script?.VoiceSegments.Count ?? 0 : images.Count,
            HasAudio = hasAudio,
            HasAnimatedClips = clipCount > 0,
            AllowProduceWithStaticImages = allowStaticProduce,
            VideoGenerationSupported = videoGenerationSupported,
            IsVoiceCode = isVoiceCode,
            VoiceCodeSegmentsWithMedia = voiceCodeMediaCount,
            RejectionReason = project.RejectionReason,
            IsBusy = activeOp.HasValue,
            ActiveOperation = activeOp.HasValue ? coordinator.Describe(activeOp.Value) : null,
            MinSegmentsForProduce = rules.MinSegmentsForProduce,
            MaxVideoDurationSec = rules.MaxVideoDurationSec,
        };

        return ProjectWorkflowResolver.Resolve(input);
    }
}
