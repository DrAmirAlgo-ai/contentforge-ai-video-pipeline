using ContentEmpireSystem.Core.DTOs;

namespace ContentEmpireSystem.Core;

public static class MotionPromptHelper
{
    public static string ResolveEffective(ScriptOutputDto script, int sortOrder, bool isTextToVideo)
    {
        var video = script.VideoSuggestions.FirstOrDefault(v =>
            v.SortOrder == sortOrder || v.LinkedImageSortOrder == sortOrder);
        var image = isTextToVideo
            ? null
            : script.ImageSuggestions.FirstOrDefault(i => i.SortOrder == sortOrder);

        var motion = (video?.UserMotionPrompt ?? image?.UserMotionPrompt ?? "").Trim();
        if (motion.Length > 0)
            return motion;

        return WanMotionPromptComposer.Compose(image, video);
    }

    public static bool AreComplete(ScriptOutputDto? script, ScriptProductionPlanDto? plan, bool isTextToVideo)
    {
        if (script is null || plan is null)
            return false;

        var expected = isTextToVideo
            ? ProductionPlanResolver.ResolveVideoSegmentCount(plan, script)
            : ProductionPlanResolver.ResolveImageSegmentCount(plan, script);

        if (expected <= 0)
            return false;

        for (var sort = 1; sort <= expected; sort++)
        {
            if (string.IsNullOrWhiteSpace(ResolveEffective(script, sort, isTextToVideo)))
                return false;
        }

        return true;
    }
}
