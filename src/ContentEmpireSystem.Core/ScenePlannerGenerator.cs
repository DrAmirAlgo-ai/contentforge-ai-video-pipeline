using ContentEmpireSystem.Core.DTOs;

namespace ContentEmpireSystem.Core;

/// <summary>Builds prompts for the scene-planner step — derives scene count and FLUX/Wan fields from narration.</summary>
public static class ScenePlannerGenerator
{
    public static string BuildUserPrompt(
        ScriptOutputDto narration,
        ScriptProductionPlanDto plan,
        bool isTextToVideo,
        string? userTemplate = null)
    {
        var template = string.IsNullOrWhiteSpace(userTemplate)
            ? DefaultUserTemplate(isTextToVideo)
            : userTemplate;

        return ApplyUserTemplate(template, narration, plan, isTextToVideo);
    }

    public static string DefaultUserTemplate(bool isTextToVideo) =>
        isTextToVideo
            ? JsonPromptDefaults.ScenePlannerUserTemplateTextToVideo
            : JsonPromptDefaults.ScenePlannerUserTemplateImageToVideo;

    public static string ApplyUserTemplate(
        string template,
        ScriptOutputDto narration,
        ScriptProductionPlanDto plan,
        bool isTextToVideo)
    {
        var narrationText = Truncate(narration.FullNarrationText, 50000);
        var hook = narration.PrimaryHook?.Trim() ?? "";
        var workflowDescription = isTextToVideo
            ? "TextToVideo (Wan T2V — no FLUX images)."
            : "ImageToVideo (FLUX images → Wan TI2V).";

        return template
            .Replace("{{workflowPathDescription}}", workflowDescription)
            .Replace("{{targetWordCount}}", plan.TargetWordCount.ToString())
            .Replace("{{actualWordCount}}", LongScriptNarrationPlanner.CountWords(narration.FullNarrationText).ToString())
            .Replace("{{primaryHook}}", hook)
            .Replace("{{fullNarrationText}}", narrationText);
    }

    public static void MergeIntoScript(ScriptOutputDto target, ScriptOutputDto plannerOutput, bool isTextToVideo)
    {
        if (isTextToVideo)
        {
            target.ImageSuggestions = [];
            target.VideoSuggestions = plannerOutput.VideoSuggestions
                .OrderBy(v => v.SortOrder)
                .ToList();
        }
        else
        {
            target.ImageSuggestions = plannerOutput.ImageSuggestions
                .OrderBy(i => i.SortOrder)
                .ToList();
            target.VideoSuggestions = plannerOutput.VideoSuggestions
                .OrderBy(v => v.SortOrder)
                .ToList();
        }
    }

    private static string Truncate(string text, int max) =>
        string.IsNullOrEmpty(text) || text.Length <= max ? text : text[..max] + "…";
}
