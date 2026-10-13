using ContentEmpireSystem.Core.DTOs;

namespace ContentEmpireSystem.Core;

/// <summary>Prompt builders for narration-first script generation.</summary>
public static class ScriptTwoPhaseGenerator
{
    public static string BuildNarrationPhasePrompt(
        VideoProjectContext ctx,
        ScriptProductionPlanDto plan,
        int wordTarget,
        bool useScriptPromptOnly) =>
        BuildNarrationChunkPrompt(
            ctx, plan, chunkIndex: 1, chunkCount: 1, chunkWordTarget: wordTarget,
            previousTail: null, isFirstChunk: true, isLastChunk: true, useScriptPromptOnly);

    public static string BuildNarrationChunkPrompt(
        VideoProjectContext ctx,
        ScriptProductionPlanDto plan,
        int chunkIndex,
        int chunkCount,
        int chunkWordTarget,
        string? previousTail,
        bool isFirstChunk,
        bool isLastChunk,
        bool useScriptPromptOnly)
    {
        var topicBlock = useScriptPromptOnly
            ? "Script-only mode: Use the Script System Prompt as the single source of truth."
            : $"Confirmed idea: {ctx.ConfirmedIdea}\nTopic category: {ctx.CategoryName}\nCategory description: {ctx.CategoryDescription}";

        if (!isFirstChunk)
        {
            var tail = string.IsNullOrWhiteSpace(previousTail) ? "(start of script)" : previousTail;
            return $"""
                {ContentLanguageHelper.BuildScriptPromptBlock(ctx.ContentLanguage)}
                {topicBlock}

                LONG SCRIPT — NARRATION CHUNK {chunkIndex}/{chunkCount} (continuation only):
                - Full script target: ~{plan.TargetWordCount} words total. This chunk: aim for ~{chunkWordTarget} words (flexible).
                - Continue seamlessly from the previous narration. Do NOT repeat the hook or recap.
                - Advance the topic/story with new substance — no filler, no summaries of prior text.
                {(isLastChunk ? "- End this chunk with a natural conclusion and CTA." : "- Do NOT conclude yet — more chunks follow.")}
                - Previous narration ending (continue directly from here):
                  \"\"\"{tail}\"\"\"

                Return ONE JSON object ONLY with key: fullNarrationText (this chunk's words only).
                Set imageSuggestions and videoSuggestions to [].
                Escape every double-quote inside strings as \\". No markdown.
                """;
        }

        var metadataKeys = chunkCount == 1
            ? """
              viewerBenefit, seo (title, description, hashtags), primaryHook, extendedHook,
              body (partOneProblem, partTwoSolution), conclusionAndCta, fullNarrationText,
              """
            : "viewerBenefit, seo (title, description, hashtags), primaryHook, extendedHook, body (partOneProblem, partTwoSolution), fullNarrationText,";

        var lengthLine = chunkCount == 1
            ? $"""
            - Target narration length: approximately {chunkWordTarget} words in fullNarrationText (flexible — shorter or longer is OK).
            """
            : $"""
            - LONG SCRIPT chunk 1/{chunkCount} for a ~{plan.TargetWordCount}-word script.
            - Write about {chunkWordTarget} words in fullNarrationText (opening/hook/body start — flexible length).
            - Do NOT write the full script in this chunk — later chunks will continue.
            """;

        return $"""
            {ContentLanguageHelper.BuildScriptPromptBlock(ctx.ContentLanguage)}
            {(useScriptPromptOnly ? "Script-only mode: Use the Script System Prompt as the single source of truth." : topicBlock)}

            NARRATION ONLY (no scene arrays — scenes are planned in a separate step):
            {lengthLine}
            - Do NOT mention time, seconds, or duration — word count is the only length constraint.
            - Return ONE JSON object ONLY with keys:
              {metadataKeys}
              productionPlan echoing: targetWordCount={plan.TargetWordCount}, workflowPath="{plan.WorkflowPath}".
            - Set imageSuggestions and videoSuggestions to empty arrays [].
            - Escape every double-quote inside strings as \\". No markdown, no text outside JSON.
            """;
    }

    public static void MergeNarrationMetadata(ScriptOutputDto target, ScriptOutputDto chunk)
    {
        if (!string.IsNullOrWhiteSpace(chunk.ConclusionAndCta))
            target.ConclusionAndCta = chunk.ConclusionAndCta;

        if (!string.IsNullOrWhiteSpace(chunk.Body.PartTwoSolution))
            target.Body.PartTwoSolution = chunk.Body.PartTwoSolution;

        if (!string.IsNullOrWhiteSpace(chunk.ExtendedHook) && string.IsNullOrWhiteSpace(target.ExtendedHook))
            target.ExtendedHook = chunk.ExtendedHook;
    }
}

public sealed class VideoProjectContext
{
    public required string ContentLanguage { get; init; }
    public string? ConfirmedIdea { get; init; }
    public string CategoryName { get; init; } = "General";
    public string CategoryDescription { get; init; } = "";
}
