using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Entities;

namespace ContentEmpireSystem.Core;

public static class ProductionPlanResolver
{
    public static ScriptProductionPlanDto BuildPlan(
        int targetWordCount,
        int imageSegmentCount,
        int videoSegmentCount,
        string? workflowPath)
    {
        if (imageSegmentCount <= 0 && videoSegmentCount <= 0)
            return BuildWordCountPlan(targetWordCount, workflowPath);

        var words = ScriptGenerationHelper.ClampWordCount(targetWordCount);
        var path = NormalizeWorkflowPath(workflowPath);
        var images = ScriptGenerationHelper.ClampSegmentCount(imageSegmentCount);
        var videos = ScriptGenerationHelper.ClampSegmentCount(videoSegmentCount);
        var estimatedSec = ScriptGenerationHelper.EstimateDurationSec(words);
        var primarySegments = IsTextToVideo(path) ? videos : images;

        return new ScriptProductionPlanDto
        {
            TargetWordCount = words,
            TargetDurationSec = estimatedSec,
            EstimatedDurationSec = estimatedSec,
            ImageSegmentCount = images,
            VideoSegmentCount = videos,
            SegmentDurationSec = primarySegments > 0
                ? ScriptGenerationHelper.SegmentDurationSec(words, primarySegments)
                : 0,
            WorkflowPath = path,
        };
    }

    public static ScriptProductionPlanDto BuildWordCountPlan(int targetWordCount, string? workflowPath)
    {
        var words = ScriptGenerationHelper.ClampWordCount(targetWordCount);
        var estimatedSec = ScriptGenerationHelper.EstimateDurationSec(words);
        return new ScriptProductionPlanDto
        {
            TargetWordCount = words,
            TargetDurationSec = estimatedSec,
            EstimatedDurationSec = estimatedSec,
            ImageSegmentCount = 0,
            VideoSegmentCount = 0,
            SegmentDurationSec = 0,
            WorkflowPath = NormalizeWorkflowPath(workflowPath),
        };
    }

    /// <summary>Fills plan snapshot from script content and policy after script/scene generation.</summary>
    public static void FinalizePlan(
        ScriptProductionPlanDto plan,
        ScriptOutputDto script,
        int maxVideoDurationSec = 0)
    {
        if (script.IsVoiceCode)
        {
            plan.ImageSegmentCount = 0;
            plan.VideoSegmentCount = Math.Max(script.VoiceSegments.Count, 0);
            plan.WorkflowPath = "VoiceCode";
        }
        else
        {
            SyncSegmentCountsFromScript(plan, script);
        }

        var actualWords = string.IsNullOrWhiteSpace(script.FullNarrationText)
            ? 0
            : LongScriptNarrationPlanner.CountWords(script.FullNarrationText);
        plan.ActualWordCount = actualWords;

        var wordBasis = actualWords > 0 ? actualWords : plan.TargetWordCount;
        var estimatedSec = ScriptGenerationHelper.EstimateDurationSec(wordBasis);
        plan.EstimatedDurationSec = estimatedSec;
        plan.TargetDurationSec = estimatedSec;

        var segmentCount = script.IsVoiceCode
            ? plan.VideoSegmentCount
            : IsTextToVideo(plan.WorkflowPath)
                ? ResolveVideoSegmentCount(plan, script)
                : ResolveImageSegmentCount(plan, script);

        plan.SegmentDurationSec = segmentCount > 0
            ? ScriptGenerationHelper.SegmentDurationSec(wordBasis, segmentCount)
            : 0;

        if (maxVideoDurationSec > 0)
            plan.MaxVideoDurationSec = maxVideoDurationSec;
    }

    public static int ResolveEstimatedDurationSec(ScriptProductionPlanDto? plan, ScriptOutputDto? script)
    {
        if (script != null && !string.IsNullOrWhiteSpace(script.FullNarrationText))
            return ScriptGenerationHelper.EstimateDurationSec(
                LongScriptNarrationPlanner.CountWords(script.FullNarrationText));

        if (plan?.EstimatedDurationSec > 0)
            return plan.EstimatedDurationSec;

        if (plan?.TargetDurationSec > 0)
            return plan.TargetDurationSec;

        if (plan?.ActualWordCount > 0)
            return ScriptGenerationHelper.EstimateDurationSec(plan.ActualWordCount);

        return plan?.TargetWordCount > 0
            ? ScriptGenerationHelper.EstimateDurationSec(plan.TargetWordCount)
            : 0;
    }

    public static void SyncSegmentCountsFromScript(ScriptProductionPlanDto plan, ScriptOutputDto script)
    {
        if (IsTextToVideo(plan.WorkflowPath))
        {
            plan.ImageSegmentCount = 0;
            plan.VideoSegmentCount = Math.Max(script.VideoSuggestions.Count, 0);
        }
        else
        {
            plan.ImageSegmentCount = Math.Max(script.ImageSuggestions.Count, 0);
            plan.VideoSegmentCount = Math.Max(
                script.VideoSuggestions.Count,
                script.ImageSuggestions.Count);
        }
    }

    public static int ResolveImageSegmentCount(ScriptProductionPlanDto? plan, ScriptOutputDto? script)
    {
        if (script?.ImageSuggestions.Count > 0)
            return script.ImageSuggestions.Count;

        if (plan?.ImageSegmentCount > 0)
            return plan.ImageSegmentCount;

        return 0;
    }

    public static int ResolveVideoSegmentCount(ScriptProductionPlanDto? plan, ScriptOutputDto? script)
    {
        if (script?.VideoSuggestions.Count > 0)
            return script.VideoSuggestions.Count;

        if (plan?.VideoSegmentCount > 0)
            return plan.VideoSegmentCount;

        return 0;
    }

    public static ScriptProductionPlanDto Resolve(VideoProject project, ScriptOutputDto? script)
    {
        if (script?.ProductionPlan is { TargetWordCount: > 0 } embedded)
        {
            if (embedded.ImageSegmentCount <= 0 && embedded.VideoSegmentCount <= 0 && script is not null)
                SyncSegmentCountsFromScript(embedded, script);

            return embedded;
        }

        var wordCount = project.TargetWordCount > 0
            ? project.TargetWordCount
            : ScriptGenerationHelper.DefaultWordCount;

        var plan = BuildWordCountPlan(wordCount, project.ProductionWorkflowPath);

        if (script is not null)
            SyncSegmentCountsFromScript(plan, script);
        else if (project.ImageSegmentCount > 0 || project.VideoSegmentCount > 0)
        {
            plan.ImageSegmentCount = project.ImageSegmentCount;
            plan.VideoSegmentCount = project.VideoSegmentCount;
        }

        return plan;
    }

    public static void ApplyToProject(VideoProject project, ScriptProductionPlanDto plan)
    {
        project.TargetWordCount = plan.TargetWordCount;
        project.TargetDurationSec = plan.EstimatedDurationSec > 0
            ? plan.EstimatedDurationSec
            : plan.TargetDurationSec;
        project.ImageSegmentCount = plan.ImageSegmentCount;
        project.VideoSegmentCount = plan.VideoSegmentCount;
        project.ProductionWorkflowPath = plan.WorkflowPath;
    }

    public static int ExpectedImageCount(ScriptProductionPlanDto plan, ScriptOutputDto? script = null) =>
        ResolveImageSegmentCount(plan, script);

    public static int ExpectedVideoCount(ScriptProductionPlanDto plan, ScriptOutputDto? script = null) =>
        ResolveVideoSegmentCount(plan, script);

    public static int SegmentDurationMs(ScriptProductionPlanDto plan, ScriptOutputDto? script = null)
    {
        var count = IsTextToVideo(plan.WorkflowPath)
            ? ResolveVideoSegmentCount(plan, script)
            : ResolveImageSegmentCount(plan, script);

        if (count <= 0)
            return ScriptGenerationHelper.SegmentDurationMs(plan.TargetWordCount, 1);

        return ScriptGenerationHelper.SegmentDurationMs(plan.TargetWordCount, count);
    }

    public static bool IsTextToVideo(string? workflowPath) =>
        string.Equals(NormalizeWorkflowPath(workflowPath), "TextToVideo", StringComparison.OrdinalIgnoreCase);

    public static string NormalizeWorkflowPath(string? workflowPath) =>
        string.Equals(workflowPath?.Trim(), "TextToVideo", StringComparison.OrdinalIgnoreCase)
            ? "TextToVideo"
            : "ImageToVideo";
}
