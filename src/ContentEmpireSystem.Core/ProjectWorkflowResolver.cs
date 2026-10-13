using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Enums;

namespace ContentEmpireSystem.Core;

public static class ProjectWorkflowResolver
{
    public static ProjectWorkflowStateDto Resolve(ProjectWorkflowInput input)
    {
        var path = ProductionPlanResolver.NormalizeWorkflowPath(input.ProductionWorkflowPath);
        var isTextToVideo = ProductionPlanResolver.IsTextToVideo(path);
        var plan = input.Script?.ProductionPlan
            ?? (input.Script is not null
                ? ProductionPlanResolver.BuildWordCountPlan(input.TargetWordCount, path)
                : null);

        if (plan is not null && input.Script is not null)
            ProductionPlanResolver.SyncSegmentCountsFromScript(plan, input.Script);

        var blockers = new List<string>();
        var completed = new List<ProjectWorkflowStep>();

        if (input.Status == VideoProjectStatus.Published)
        {
            return Build(input, path, isTextToVideo, plan, ProjectWorkflowStep.Done,
                "اكتمل — الفيديو النهائي جاهز", completed, blockers, canProduce: false);
        }

        if (input.Status == VideoProjectStatus.Failed)
        {
            if (!string.IsNullOrWhiteSpace(input.RejectionReason))
                blockers.Add(input.RejectionReason!);
            return Build(input, path, isTextToVideo, plan, ProjectWorkflowStep.Failed,
                "فشل الإنتاج — راجع السبب وأعد المحاولة", completed, blockers, canProduce: input.SegmentCount >= input.MinSegmentsForProduce);
        }

        if (input.Status == VideoProjectStatus.Processing)
        {
            return Build(input, path, isTextToVideo, plan, ProjectWorkflowStep.Produce,
                "جاري تركيب الفيديو (FFmpeg)…", completed, blockers, canProduce: false);
        }

        // Idea
        if (input.Status < VideoProjectStatus.IdeaConfirmed
            || (string.IsNullOrWhiteSpace(input.ConfirmedIdea) && string.IsNullOrWhiteSpace(input.CurrentIdea)))
        {
            return Build(input, path, isTextToVideo, plan, ProjectWorkflowStep.Idea,
                "ولّد فكرة ثم أكّدها", completed, blockers, canProduce: false);
        }
        completed.Add(ProjectWorkflowStep.Idea);

        // Script
        if (input.Script is null || input.Status < VideoProjectStatus.ScriptGenerated)
        {
            return Build(input, path, isTextToVideo, plan, ProjectWorkflowStep.Script,
                "ولّد السكريبت من الفكرة المؤكدة", completed, blockers, canProduce: false);
        }
        completed.Add(ProjectWorkflowStep.Script);

        if (input.IsVoiceCode)
            return ResolveVoiceCodePath(input, path, isTextToVideo, plan, completed, blockers);

        if (!isTextToVideo)
        {
            var staticCount = input.StaticImageCount;
            var clipCount = input.VideoClipCount;
            var requiredImages = ProductionPlanResolver.ResolveImageSegmentCount(plan, input.Script)
                is var req && req > 0 ? req : plan?.ImageSegmentCount ?? input.ImageSegmentCount;
            var totalMedia = staticCount + clipCount;
            if (totalMedia < requiredImages)
            {
                blockers.Add($"مطلوب {requiredImages} صورة FLUX — الموجود {totalMedia}");
                return Build(input, path, isTextToVideo, plan, ProjectWorkflowStep.FluxImages,
                    "ولّد صور FLUX", completed, blockers, canProduce: false);
            }
            completed.Add(ProjectWorkflowStep.FluxImages);
        }

        if (RequiresMotionPrompts(input))
        {
            if (!MotionPromptHelper.AreComplete(input.Script, plan, isTextToVideo))
            {
                blockers.Add(isTextToVideo
                    ? "راجع Motion Prompts لكل مقطع T2V واحفظها"
                    : "راجع Motion Prompts لكل مشهد واحفظها قبل Animate");
                return Build(input, path, isTextToVideo, plan, ProjectWorkflowStep.MotionPrompts,
                    blockers[^1], completed, blockers, canProduce: false);
            }
            completed.Add(ProjectWorkflowStep.MotionPrompts);
        }

        if (isTextToVideo && !input.VideoGenerationSupported)
        {
            blockers.Add("وضع أونلاين لا يدعم Wan T2V — استخدم مسار صور ثم Ken Burns");
            return Build(input, path, isTextToVideo, plan, ProjectWorkflowStep.Script,
                blockers[^1], completed, blockers, canProduce: false);
        }

        if (isTextToVideo)
        {
            var clipCount = input.VideoClipCount;
            var required = ProductionPlanResolver.ResolveVideoSegmentCount(plan, input.Script)
                is var req && req > 0 ? req : plan?.VideoSegmentCount ?? input.VideoSegmentCount;
            if (clipCount < required)
            {
                blockers.Add($"مطلوب {required} مقطع Wan T2V — الموجود {clipCount}");
                return Build(input, path, isTextToVideo, plan, ProjectWorkflowStep.WanVideo,
                    "ولّد مقاطع Wan T2V", completed, blockers, canProduce: false);
            }
        }
        else if (input.VideoGenerationSupported && !input.AllowProduceWithStaticImages)
        {
            var requiredClips = ProductionPlanResolver.ResolveImageSegmentCount(plan, input.Script)
                is var imgReq && imgReq > 0 ? imgReq : plan?.ImageSegmentCount ?? input.ImageSegmentCount;
            if (input.VideoClipCount < requiredClips)
            {
                blockers.Add($"حوّل الصور إلى مقاطع Wan TI2V ({input.VideoClipCount}/{requiredClips})");
                return Build(input, path, isTextToVideo, plan, ProjectWorkflowStep.WanVideo,
                    "Animate Images — Wan TI2V", completed, blockers, canProduce: false);
            }
            completed.Add(ProjectWorkflowStep.WanVideo);
        }

        // Audio
        if (!input.HasAudio)
        {
            var nextAction = "ارفع ملف الصوت أو ولّد TTS";
            if (!isTextToVideo && input.StaticImageCount > 0 && input.VideoGenerationSupported)
                nextAction = $"باقي {input.StaticImageCount} مشهد للتحريك (Wan) — ثم الصوت";
            return Build(input, path, isTextToVideo, plan, ProjectWorkflowStep.Audio,
                nextAction, completed, blockers, canProduce: false);
        }
        completed.Add(ProjectWorkflowStep.Audio);

        // Produce readiness
        if (input.SegmentCount < input.MinSegmentsForProduce)
        {
            blockers.Add($"مطلوب {input.MinSegmentsForProduce} مشاهد على الأقل للإنتاج");
            return Build(input, path, isTextToVideo, plan, ProjectWorkflowStep.Produce,
                blockers[^1], completed, blockers, canProduce: false);
        }

        if (TryAddDurationBlocker(input, plan, blockers))
        {
            return Build(input, path, isTextToVideo, plan, ProjectWorkflowStep.Produce,
                blockers[^1], completed, blockers, canProduce: false);
        }

        return Build(input, path, isTextToVideo, plan, ProjectWorkflowStep.Produce,
            "ابدأ الإنتاج النهائي (FFmpeg)", completed, blockers, canProduce: true);
    }

    private static ProjectWorkflowStateDto ResolveVoiceCodePath(
        ProjectWorkflowInput input,
        string path,
        bool isTextToVideo,
        ScriptProductionPlanDto? plan,
        List<ProjectWorkflowStep> completed,
        List<string> blockers)
    {
        var required = input.Script?.VoiceSegments.Count ?? 0;
        if (required < input.MinSegmentsForProduce)
        {
            blockers.Add($"Voice Code يحتاج {input.MinSegmentsForProduce} مقاطع على الأقل");
            return Build(input, path, isTextToVideo, plan, ProjectWorkflowStep.Script,
                blockers[^1], completed, blockers, canProduce: false);
        }

        var withMedia = input.VoiceCodeSegmentsWithMedia;
        if (withMedia < required)
        {
            var missing = required - withMedia;
            blockers.Add($"ارفع وسائط (صورة/فيديو) لكل مقطع — ناقص {missing} من {required}");
            return Build(input, path, isTextToVideo, plan, ProjectWorkflowStep.VoiceCodeMedia,
                "ارفع وسائط Voice Code لكل مقطع [VC:n]", completed, blockers, canProduce: false);
        }
        completed.Add(ProjectWorkflowStep.VoiceCodeMedia);

        if (!input.HasAudio)
        {
            return Build(input, path, isTextToVideo, plan, ProjectWorkflowStep.Audio,
                "ارفع ملف الصوت أو ولّد TTS", completed, blockers, canProduce: false);
        }
        completed.Add(ProjectWorkflowStep.Audio);

        if (TryAddDurationBlocker(input, plan, blockers))
        {
            return Build(input, path, isTextToVideo, plan, ProjectWorkflowStep.Produce,
                blockers[^1], completed, blockers, canProduce: false);
        }

        return Build(input, path, isTextToVideo, plan, ProjectWorkflowStep.Produce,
            "ابدأ الإنتاج النهائي (FFmpeg)", completed, blockers, canProduce: true);
    }

    private static bool TryAddDurationBlocker(
        ProjectWorkflowInput input,
        ScriptProductionPlanDto? plan,
        List<string> blockers)
    {
        if (input.MaxVideoDurationSec <= 0)
            return false;

        var durationSec = input.AudioDurationSec > 0
            ? input.AudioDurationSec
            : ProductionPlanResolver.ResolveEstimatedDurationSec(plan, input.Script);

        if (!ProductionDurationValidator.ExceedsMaxDuration(durationSec, input.MaxVideoDurationSec))
            return false;

        blockers.Add(ProductionDurationValidator.FormatExceedsError(durationSec, input.MaxVideoDurationSec));
        return true;
    }

    private static bool RequiresMotionPrompts(ProjectWorkflowInput input) =>
        input.VideoGenerationSupported;

    public static bool CanRunOperation(ProjectWorkflowStateDto workflow, ProjectOperationKind operation)
    {
        if (workflow.IsBusy)
            return false;

        return operation switch
        {
            ProjectOperationKind.IdeaGeneration => !workflow.IsBusy,
            ProjectOperationKind.ScriptGeneration => workflow.CurrentStep == ProjectWorkflowStep.Script,
            ProjectOperationKind.VoiceCodeMediaUpload => workflow.IsVoiceCode
                && workflow.CurrentStep == ProjectWorkflowStep.VoiceCodeMedia,
            ProjectOperationKind.ImageGeneration => workflow.CurrentStep == ProjectWorkflowStep.FluxImages,
            ProjectOperationKind.VideoAnimation => workflow.VideoGenerationSupported
                && string.Equals(workflow.WorkflowPath, "ImageToVideo", StringComparison.OrdinalIgnoreCase)
                && !workflow.IsTextToVideo
                && workflow.StaticImageCount > 0,
            ProjectOperationKind.VideoGeneration => workflow.VideoGenerationSupported
                && workflow.CurrentStep == ProjectWorkflowStep.WanVideo
                && workflow.WorkflowPath == "TextToVideo",
            ProjectOperationKind.AudioGeneration => workflow.CurrentStep == ProjectWorkflowStep.Audio,
            ProjectOperationKind.Production => workflow.CanProduce && workflow.CurrentStep == ProjectWorkflowStep.Produce,
            _ => false
        };
    }

    private static ProjectWorkflowStateDto Build(
        ProjectWorkflowInput input,
        string path,
        bool isTextToVideo,
        ScriptProductionPlanDto? plan,
        ProjectWorkflowStep current,
        string nextActionAr,
        List<ProjectWorkflowStep> completed,
        List<string> blockers,
        bool canProduce)
    {
        var dto = new ProjectWorkflowStateDto
        {
            WorkflowPath = path,
            IsTextToVideo = isTextToVideo,
            CurrentStep = current,
            NextActionAr = nextActionAr,
            CompletedSteps = completed.Distinct().ToList(),
            Blockers = blockers.Distinct().ToList(),
            CanProduce = canProduce,
            VideoGenerationSupported = input.VideoGenerationSupported,
            IsVoiceCode = input.IsVoiceCode,
            SegmentCount = input.SegmentCount,
            RequiredSegments = input.IsVoiceCode
                ? input.Script?.VoiceSegments.Count ?? input.MinSegmentsForProduce
                : isTextToVideo
                    ? ProductionPlanResolver.ResolveVideoSegmentCount(plan, input.Script)
                    : ProductionPlanResolver.ResolveImageSegmentCount(plan, input.Script),
            StaticImageCount = input.StaticImageCount,
            VideoClipCount = input.VideoClipCount,
            RequireWanAnimation = !input.AllowProduceWithStaticImages,
            IsBusy = input.IsBusy,
            ActiveOperation = input.ActiveOperation
        };
        PopulateAllowedOperations(dto);
        return dto;
    }

    private static void PopulateAllowedOperations(ProjectWorkflowStateDto dto)
    {
        var allowed = new List<string>();
        foreach (ProjectOperationKind op in Enum.GetValues<ProjectOperationKind>())
        {
            if (CanRunOperation(dto, op))
                allowed.Add(op.ToString());
        }
        dto.AllowedOperations = allowed;
    }
}

public sealed class ProjectWorkflowInput
{
    public VideoProjectStatus Status { get; init; }
    public string ProductionWorkflowPath { get; init; } = "ImageToVideo";
    public string? CurrentIdea { get; init; }
    public string? ConfirmedIdea { get; init; }
    public ScriptOutputDto? Script { get; init; }
    public int TargetWordCount { get; init; } = ScriptGenerationHelper.DefaultWordCount;
    public int ImageSegmentCount { get; init; } = ScriptGenerationHelper.DefaultImageSegmentCount;
    public int VideoSegmentCount { get; init; } = ScriptGenerationHelper.DefaultVideoSegmentCount;
    public int StaticImageCount { get; init; }
    public int VideoClipCount { get; init; }
    public int SegmentCount { get; init; }
    public bool HasAudio { get; init; }
    public bool HasAnimatedClips { get; init; }
    public bool AllowProduceWithStaticImages { get; init; } = true;
    public bool VideoGenerationSupported { get; init; } = true;
    public bool IsVoiceCode { get; init; }
    public int VoiceCodeSegmentsWithMedia { get; init; }
    public string? RejectionReason { get; init; }
    public bool IsBusy { get; init; }
    public string? ActiveOperation { get; init; }
    public int MinSegmentsForProduce { get; init; } = ProductionRulesDto.Defaults.MinSegmentsForProduce;
    public int MaxVideoDurationSec { get; init; } = ProductionRules.MaxVideoDurationSec;
    /// <summary>Known audio file duration; 0 = use plan estimate.</summary>
    public double AudioDurationSec { get; init; }
}
