using ContentEmpireSystem.Core.DTOs;

namespace ContentEmpireSystem.Core;

public static class ScriptSegmentPlanner
{
    public static void ApplyPlanToScript(ScriptOutputDto script, ScriptProductionPlanDto plan, bool resplitSegments)
    {
        script.ProductionPlan = plan;

        if (resplitSegments && !string.IsNullOrWhiteSpace(script.FullNarrationText))
        {
            if (ProductionPlanResolver.IsTextToVideo(plan.WorkflowPath))
            {
                script.ImageSuggestions = [];
                script.VideoSuggestions = NormalizeVideoSuggestions(script, plan.VideoSegmentCount);
            }
            else
            {
                script.ImageSuggestions = NormalizeImageSuggestions(script, plan.ImageSegmentCount);
                script.VideoSuggestions = NormalizeVideoSuggestions(script, plan.ImageSegmentCount);
            }
        }

        WanMotionPromptComposer.ApplyToScript(
            script, ProductionPlanResolver.IsTextToVideo(plan.WorkflowPath));
    }

    public static void EnsureScriptMatchesPlan(ScriptOutputDto script, ScriptProductionPlanDto plan)
    {
        ProductionPlanResolver.SyncSegmentCountsFromScript(plan, script);
        script.ProductionPlan = plan;
    }

    public static List<ImageSuggestionDto> SelectImageSuggestions(
        ScriptOutputDto script, ScriptProductionPlanDto plan, int explicitCount)
    {
        EnsureScriptMatchesPlan(script, plan);
        var target = explicitCount > 0
            ? ScriptGenerationHelper.ClampSegmentCount(explicitCount)
            : plan.ImageSegmentCount;

        var ordered = script.ImageSuggestions.OrderBy(s => s.SortOrder).ToList();
        if (ordered.Count >= target)
            return ordered.Take(target).ToList();

        var chunks = SplitNarration(script.FullNarrationText, target);
        var result = ordered.ToList();
        for (var i = result.Count; i < target; i++)
        {
            var chunk = chunks.ElementAtOrDefault(i) ?? $"Scene {i + 1}";
            result.Add(new ImageSuggestionDto
            {
                SortOrder = i + 1,
                Prompt = chunk,
                SceneDescription = chunk,
                NarrationExcerpt = chunk,
            });
        }

        return result.Take(target).ToList();
    }

    public static List<VideoSuggestionDto> SelectVideoSuggestions(
        ScriptOutputDto script, ScriptProductionPlanDto plan, int explicitCount)
    {
        EnsureScriptMatchesPlan(script, plan);
        var target = explicitCount > 0
            ? ScriptGenerationHelper.ClampSegmentCount(explicitCount)
            : plan.VideoSegmentCount;

        var ordered = script.VideoSuggestions.OrderBy(v => v.SortOrder).ToList();
        if (ordered.Count == 0)
            throw new InvalidOperationException("لا توجد videoSuggestions في السكريبت. أعد توليد السكريبت.");

        return ordered.Take(Math.Clamp(target, 1, ordered.Count)).ToList();
    }

    private static List<ImageSuggestionDto> NormalizeImageSuggestions(ScriptOutputDto script, int targetCount)
    {
        if (targetCount <= 0)
            targetCount = script.ImageSuggestions.Count;

        if (targetCount <= 0)
            return script.ImageSuggestions.OrderBy(s => s.SortOrder).ToList();
        var narration = script.FullNarrationText;
        var ordered = script.ImageSuggestions.OrderBy(s => s.SortOrder).ToList();
        var chunks = SplitNarration(narration, targetCount);
        var result = new List<ImageSuggestionDto>(targetCount);

        for (var i = 0; i < targetCount; i++)
        {
            var chunk = chunks.ElementAtOrDefault(i) ?? $"Scene {i + 1}";
            var existing = ordered.ElementAtOrDefault(i);
            var item = existing ?? new ImageSuggestionDto();
            item.SortOrder = i + 1;
            item.NarrationExcerpt = string.IsNullOrWhiteSpace(item.NarrationExcerpt) ? chunk : item.NarrationExcerpt;
            item.SceneDescription = string.IsNullOrWhiteSpace(item.SceneDescription) ? chunk : item.SceneDescription;
            if (string.IsNullOrWhiteSpace(item.Prompt))
                item.Prompt = ImagePromptComposer.ComposeSceneContent(item);
            result.Add(item);
        }

        return result;
    }

    private static List<VideoSuggestionDto> NormalizeVideoSuggestions(ScriptOutputDto script, int targetCount)
    {
        if (targetCount <= 0)
            targetCount = script.VideoSuggestions.Count;

        if (targetCount <= 0)
            return script.VideoSuggestions.OrderBy(v => v.SortOrder).ToList();
        var chunks = SplitNarration(script.FullNarrationText, targetCount);
        var ordered = script.VideoSuggestions.OrderBy(v => v.SortOrder).ToList();
        var result = new List<VideoSuggestionDto>(targetCount);

        for (var i = 0; i < targetCount; i++)
        {
            var chunk = chunks.ElementAtOrDefault(i) ?? $"Scene {i + 1}";
            var existing = ordered.ElementAtOrDefault(i);
            var fromImage = script.ImageSuggestions.ElementAtOrDefault(i);
            result.Add(new VideoSuggestionDto
            {
                SortOrder = i + 1,
                LinkedImageSortOrder = i + 1,
                NarrationExcerpt = existing?.NarrationExcerpt ?? fromImage?.NarrationExcerpt ?? chunk,
                SceneDescription = existing?.SceneDescription ?? fromImage?.SceneDescription ?? chunk,
                UserMotionPrompt = existing?.UserMotionPrompt ?? fromImage?.UserMotionPrompt ?? "",
                Prompt = "",
            });
        }

        return result;
    }

    public static string ExcerptForSceneRange(string text, int totalScenes, int startSortOrder, int count)
    {
        if (string.IsNullOrWhiteSpace(text) || totalScenes <= 0 || count <= 0)
            return text ?? "";

        var chunks = SplitNarration(text, totalScenes);
        var start = Math.Max(0, startSortOrder - 1);
        var end = Math.Min(chunks.Count, start + count);
        return string.Join("\n\n", chunks.Skip(start).Take(end - start));
    }

    private static List<string> SplitNarration(string text, int parts)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Enumerable.Range(1, parts).Select(i => $"Scene {i}").ToList();

        var sentences = text.Split(['.', '!', '?', '\n', '؟', '…'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0).ToList();
        if (sentences.Count == 0)
            return Enumerable.Range(1, parts).Select(i => $"Scene {i}").ToList();

        var buckets = Enumerable.Range(0, parts).Select(_ => new List<string>()).ToList();
        for (var i = 0; i < sentences.Count; i++)
            buckets[i % parts].Add(sentences[i]);

        return buckets.Select(b => b.Count > 0 ? string.Join(". ", b) : "Scene").ToList();
    }
}
