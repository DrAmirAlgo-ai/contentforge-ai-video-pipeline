using ContentEmpireSystem.Core.DTOs;

namespace ContentEmpireSystem.Core;

/// <summary>Builds prompts for the image-scene step — writes exactly N FLUX/Wan scene prompts from narration.</summary>
public static class ImageScenePromptGenerator
{
    public static string BuildUserPrompt(
        ScriptOutputDto narration,
        ScriptProductionPlanDto plan,
        int imageSegmentCount,
        bool isTextToVideo,
        string? userTemplate = null,
        string? wanMotionPositiveRules = null,
        string? wanMotionNegativeRules = null)
    {
        var template = string.IsNullOrWhiteSpace(userTemplate)
            ? DefaultUserTemplate(isTextToVideo)
            : userTemplate;

        return ApplyUserTemplate(
            template, narration, plan, imageSegmentCount, isTextToVideo,
            wanMotionPositiveRules, wanMotionNegativeRules);
    }

    public static string DefaultUserTemplate(bool isTextToVideo) =>
        isTextToVideo
            ? JsonPromptDefaults.ImageScenesUserTemplateTextToVideo
            : JsonPromptDefaults.ImageScenesUserTemplateImageToVideo;

    public static string ApplyUserTemplate(
        string template,
        ScriptOutputDto narration,
        ScriptProductionPlanDto plan,
        int imageSegmentCount,
        bool isTextToVideo,
        string? wanMotionPositiveRules = null,
        string? wanMotionNegativeRules = null)
    {
        var narrationText = Truncate(narration.FullNarrationText, 50000);
        var hook = narration.PrimaryHook?.Trim() ?? "";
        var workflowDescription = isTextToVideo
            ? "TextToVideo (Wan T2V — no FLUX images)."
            : "ImageToVideo (FLUX images → Wan TI2V).";
        var positiveRules = string.IsNullOrWhiteSpace(wanMotionPositiveRules)
            ? WanMotionPromptDefaults.PositiveRules
            : wanMotionPositiveRules.Trim();
        var negativeRules = string.IsNullOrWhiteSpace(wanMotionNegativeRules)
            ? WanMotionPromptDefaults.NegativeRules
            : wanMotionNegativeRules.Trim();

        return template
            .Replace("{{workflowPathDescription}}", workflowDescription)
            .Replace("{{imageSegmentCount}}", imageSegmentCount.ToString())
            .Replace("{{targetWordCount}}", plan.TargetWordCount.ToString())
            .Replace("{{actualWordCount}}", LongScriptNarrationPlanner.CountWords(narration.FullNarrationText).ToString())
            .Replace("{{primaryHook}}", hook)
            .Replace("{{fullNarrationText}}", narrationText)
            .Replace("{{wanMotionPositiveRules}}", positiveRules)
            .Replace("{{wanMotionNegativeRules}}", negativeRules);
    }

    public static void MergeIntoScript(
        ScriptOutputDto target,
        ScriptOutputDto plannerOutput,
        int imageSegmentCount,
        bool isTextToVideo)
    {
        ScenePlannerGenerator.MergeIntoScript(target, plannerOutput, isTextToVideo);
        TrimToExactCount(target, imageSegmentCount, isTextToVideo);
    }

    public static void TrimToExactCount(ScriptOutputDto script, int imageSegmentCount, bool isTextToVideo)
    {
        if (isTextToVideo)
        {
            script.ImageSuggestions = [];
            script.VideoSuggestions = script.VideoSuggestions
                .OrderBy(v => v.SortOrder)
                .Take(imageSegmentCount)
                .ToList();
            script.ProductionPlan ??= new ScriptProductionPlanDto();
            script.ProductionPlan.VideoSegmentCount = script.VideoSuggestions.Count;
            script.ProductionPlan.ImageSegmentCount = 0;
            return;
        }

        script.ImageSuggestions = script.ImageSuggestions
            .OrderBy(i => i.SortOrder)
            .Take(imageSegmentCount)
            .ToList();
        script.VideoSuggestions = script.VideoSuggestions
            .OrderBy(v => v.SortOrder)
            .Take(imageSegmentCount)
            .ToList();
        script.ProductionPlan ??= new ScriptProductionPlanDto();
        script.ProductionPlan.ImageSegmentCount = script.ImageSuggestions.Count;
        script.ProductionPlan.VideoSegmentCount = script.VideoSuggestions.Count;
    }

    public static bool HasExactSceneCount(ScriptOutputDto script, int imageSegmentCount, bool isTextToVideo)
    {
        if (imageSegmentCount <= 0)
            return false;

        return isTextToVideo
            ? script.VideoSuggestions.Count == imageSegmentCount
            : script.ImageSuggestions.Count == imageSegmentCount
                && script.VideoSuggestions.Count == imageSegmentCount;
    }

    private static string Truncate(string text, int max) =>
        string.IsNullOrEmpty(text) || text.Length <= max ? text : text[..max] + "…";
}
