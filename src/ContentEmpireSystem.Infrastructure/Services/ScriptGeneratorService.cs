using System.Text.Json;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Entities;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Data;
using ContentEmpireSystem.Infrastructure.Services.Ai;
using Microsoft.EntityFrameworkCore;

namespace ContentEmpireSystem.Infrastructure.Services;

public class ScriptGeneratorService(
    ApplicationDbContext db,
    AiChatService aiChat,
    AiProviderResolver providerResolver,
    ISystemSettingsService settings,
    SystemActivityService activity,
    IProjectWorkflowBuilder workflowBuilder) : IScriptGeneratorService
{
    private static readonly JsonSerializerOptions JsonOptions = LlmJsonParser.DefaultOptions;

    private const string JsonRetrySuffix = """

        CRITICAL (retry): Your previous reply was not valid JSON. Return ONE compact JSON object only.
        Escape double quotes inside strings. No markdown, no comments, no text before or after the JSON.
        """;

    public async Task<VideoProjectDto> GenerateScriptAsync(
        Guid projectId,
        string? language = null,
        bool useScriptPromptOnly = false,
        int targetWordCount = ScriptGenerationHelper.DefaultWordCount,
        int imageSegmentCount = 0,
        string? productionWorkflowPath = null,
        CancellationToken ct = default)
    {
        try
        {
            var project = await db.VideoProjects
                .Include(p => p.Category)
                .Include(p => p.GeneratedImages)
                .FirstOrDefaultAsync(p => p.Id == projectId, ct);

            if (project is null)
                throw new InvalidOperationException(
                    "المشروع غير موجود أو لا تملك صلاحية الوصول إليه. أعد تحميل الصفحة أو أنشئ مشروعاً جديداً.");

            useScriptPromptOnly = ScriptPromptResolver.ResolveScriptPromptOnlyMode(useScriptPromptOnly, project.CategoryId);

            if (!useScriptPromptOnly && string.IsNullOrWhiteSpace(project.ConfirmedIdea))
                throw new InvalidOperationException("Confirm an idea before generating script.");

            if (!useScriptPromptOnly && !project.CategoryId.HasValue)
                throw new InvalidOperationException("Select a content niche, or choose Script Prompt Only mode.");

            project.ContentLanguage = await ContentLanguageHelper.ResolveForProjectAsync(
                project.ContentLanguage, language, settings.GetOrDefaultAsync, ct);

            var rules = await settings.GetProductionRulesAsync(ct);
            var wordTarget = rules.ClampWordCount(targetWordCount);
            var workflowPath = await ResolveWorkflowPathAsync(project, productionWorkflowPath, ct);
            var plan = ProductionPlanResolver.BuildWordCountPlan(wordTarget, workflowPath);
            ProductionPlanResolver.ApplyToProject(project, plan);
            var isTextToVideo = ProductionPlanResolver.IsTextToVideo(plan.WorkflowPath);
            var chunkedLabel = LongScriptNarrationPlanner.RequiresChunkedNarration(wordTarget)
                ? $"توليد السكريبت ({LongScriptNarrationPlanner.BuildChunkWordTargets(wordTarget).Count} دفعات)"
                : "توليد السكريبت";
            var scriptConfig = await providerResolver.ResolveScriptProviderAsync(ct);
            activity.Start(projectId, chunkedLabel, ScriptProviderNames.DisplayLabel(scriptConfig.Provider));

            var ctx = new VideoProjectContext
            {
                ContentLanguage = project.ContentLanguage,
                ConfirmedIdea = project.ConfirmedIdea,
                CategoryName = project.Category?.NameEn ?? project.Category?.NameAr ?? "General",
                CategoryDescription = project.Category?.Description ?? ""
            };

            var (script, provider) = await GenerateNarrationAsync(ctx, plan, wordTarget, useScriptPromptOnly, ct);

            if (script is null || !ScriptJsonNormalizer.HasUsableContent(script))
                throw new InvalidOperationException(
                    $"السكريبت فارغ — {provider} لم يملأ fullNarrationText. راجع إعدادات المزود.");

            var segmentCount = ResolveImageSegmentCount(imageSegmentCount, project.ImageSegmentCount, rules.DefaultImageSegmentCount);
            ScriptGenerationHelper.ApplySceneCountsFromSettings(plan, segmentCount, plan.WorkflowPath);

            var sceneProvider = await GenerateImageScenesAsync(
                script, plan, segmentCount, isTextToVideo, rules.MinSegmentsForProduce, ct);

            if (sceneProvider is null
                || !ImageScenePromptGenerator.HasExactSceneCount(script, segmentCount, isTextToVideo)
                || !ScriptJsonNormalizer.HasRequiredScenes(script, plan, rules.MinSegmentsForProduce))
            {
                throw new InvalidOperationException(
                    $"توليد برومبتات الصور فشل — مطلوب {segmentCount} مشهد/صورة. راجع برومبت «برومبتات الصور» أو أعد التوليد.");
            }

            if (!string.IsNullOrWhiteSpace(sceneProvider))
                provider = sceneProvider;

            ProductionPlanResolver.FinalizePlan(plan, script, rules.MaxVideoDurationSec);
            script.ProductionPlan = plan;
            ProductionPlanResolver.ApplyToProject(project, plan);
            ScriptSegmentPlanner.ApplyPlanToScript(script, plan, resplitSegments: false);

            project.ScriptJson = JsonSerializer.Serialize(script, JsonOptions);
            project.AiProviderUsed = provider;
            project.ProductionWorkflowPath = plan.WorkflowPath;
            project.Status = VideoProjectStatus.ScriptGenerated;
            project.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var wordCount = LongScriptNarrationPlanner.CountWords(script.FullNarrationText);
            var sceneCount = isTextToVideo
                ? script.VideoSuggestions.Count
                : script.ImageSuggestions.Count;
            var lengthNote = wordCount == wordTarget
                ? $"{wordCount} كلمة"
                : $"{wordCount} كلمة (هدف ~{wordTarget})";
            activity.Complete(projectId, $"سكريبت جاهز · {provider} — {lengthNote} · {sceneCount} مشهد");

            var saved = await db.VideoProjects
                .Include(p => p.Category)
                .Include(p => p.GeneratedImages)
                .AsNoTracking()
                .FirstAsync(p => p.Id == projectId, ct);
            return await VideoProjectMapper.MapWithWorkflowAsync(saved, workflowBuilder, ct);
        }
        catch (Exception ex)
        {
            activity.Fail(projectId, ex.Message);
            throw;
        }
    }

    private async Task<(ScriptOutputDto? Script, string Provider)> GenerateNarrationAsync(
        VideoProjectContext ctx,
        ScriptProductionPlanDto plan,
        int wordTarget,
        bool useScriptPromptOnly,
        CancellationToken ct)
    {
        if (LongScriptNarrationPlanner.RequiresChunkedNarration(wordTarget))
            return await GenerateChunkedNarrationAsync(ctx, plan, wordTarget, useScriptPromptOnly, ct);

        int? maxOutputTokens = LongScriptNarrationPlanner.RequiresChunkedNarration(wordTarget)
            ? null
            : ScriptGenerationHelper.SinglePassMaxOutputTokens;
        var narrationPrompt = ScriptTwoPhaseGenerator.BuildNarrationPhasePrompt(
            ctx, plan, wordTarget, useScriptPromptOnly);
        var (script, provider) = await GenerateNarrationChunkAsync(
            narrationPrompt, useScriptPromptOnly, maxOutputTokens, ct);

        if (script is null || string.IsNullOrWhiteSpace(script.FullNarrationText))
        {
            var fallbackPrompt = BuildNarrationOnlyScriptPrompt(ctx, plan, wordTarget, useScriptPromptOnly);
            (script, provider) = await GenerateNarrationChunkAsync(
                fallbackPrompt, useScriptPromptOnly, maxOutputTokens, ct);
        }

        if (script is not null)
            script.FullNarrationText = LongScriptNarrationPlanner.CapChunkText(
                script.FullNarrationText, wordTarget);

        return (script, provider);
    }

    private async Task<string?> GenerateImageScenesAsync(
        ScriptOutputDto script,
        ScriptProductionPlanDto plan,
        int imageSegmentCount,
        bool isTextToVideo,
        int minSegments,
        CancellationToken ct)
    {
        script.ImageSuggestions = [];
        script.VideoSuggestions = [];

        var templateKey = isTextToVideo
            ? SettingKeys.ImageScenesUserTemplateTextToVideo
            : SettingKeys.ImageScenesUserTemplate;
        var defaultTemplate = ImageScenePromptGenerator.DefaultUserTemplate(isTextToVideo);
        var userTemplate = await settings.GetOrDefaultAsync(templateKey, defaultTemplate, ct);
        var positiveRules = await settings.GetOrDefaultAsync(
            SettingKeys.WanMotionPositivePrompt, WanMotionPromptDefaults.PositiveRules, ct);
        var negativeRules = await settings.GetOrDefaultAsync(
            SettingKeys.WanMotionNegativePrompt, WanMotionPromptDefaults.NegativeRules, ct);
        var userPrompt = ImageScenePromptGenerator.BuildUserPrompt(
            script, plan, imageSegmentCount, isTextToVideo, userTemplate, positiveRules, negativeRules);
        var (json, provider) = await GenerateImageScenesJsonAsync(userPrompt, ct);
        var scenes = TryParseScriptJson(json);

        if (scenes is null
            || !ImageScenePromptGenerator.HasExactSceneCount(scenes, imageSegmentCount, isTextToVideo)
            || !ScriptJsonNormalizer.HasRequiredScenes(scenes, plan, minSegments))
        {
            var (retryJson, retryProvider) = await GenerateImageScenesJsonAsync(userPrompt + JsonRetrySuffix, ct);
            provider = retryProvider;
            scenes = TryParseScriptJson(retryJson);
        }

        if (scenes is null
            || !ImageScenePromptGenerator.HasExactSceneCount(scenes, imageSegmentCount, isTextToVideo)
            || !ScriptJsonNormalizer.HasRequiredScenes(scenes, plan, minSegments))
            return null;

        ImageScenePromptGenerator.MergeIntoScript(script, scenes, imageSegmentCount, isTextToVideo);
        return provider;
    }

    private Task<(string Json, string Provider)> GenerateImageScenesJsonAsync(string userPrompt, CancellationToken ct) =>
        aiChat.GenerateJsonAsync(
            userPrompt,
            SettingKeys.ImageScenesPromptInstructions,
            SettingKeys.ImageScenesPromptJsonSchema,
            SettingKeys.ImageScenesPrompt,
            ct: ct);

    private Task<(string Json, string Provider)> GenerateScriptJsonAsync(
        string userPrompt, bool useScriptPromptOnly, int? maxOutputTokens, CancellationToken ct) =>
        ScriptPromptResolver.UsesCustomScriptSystemPrompt(useScriptPromptOnly)
            ? aiChat.GenerateJsonAsync(
                userPrompt,
                SettingKeys.ScriptSystemPromptInstructions,
                SettingKeys.ScriptSystemPromptJsonSchema,
                SettingKeys.ScriptSystemPrompt,
                maxOutputTokens,
                ct)
            : aiChat.GenerateJsonWithBuiltInAsync(userPrompt, maxOutputTokens, ct);

    private static string BuildNarrationOnlyScriptPrompt(
        VideoProjectContext ctx,
        ScriptProductionPlanDto plan,
        int wordTarget,
        bool useScriptPromptOnly) =>
        $"""
        {ContentLanguageHelper.BuildScriptPromptBlock(ctx.ContentLanguage)}
        {(useScriptPromptOnly
            ? "Script-only mode: follow Script System Prompt topic."
            : $"Confirmed idea: {ctx.ConfirmedIdea}\nTopic category: {ctx.CategoryName}")}
        Target narration: approximately {wordTarget} words in fullNarrationText (flexible — any reasonable length is OK).
        Return ONE minimal JSON object ONLY (no scene arrays) with keys:
        fullNarrationText, primaryHook, body.partOneProblem, body.partTwoSolution, conclusionAndCta,
        productionPlan with targetWordCount={wordTarget} and workflowPath="{plan.WorkflowPath}".
        Set imageSuggestions and videoSuggestions to empty arrays [].
        Escape every double-quote inside strings as \\". No markdown.
        """;

    private static ScriptOutputDto? TryParseScriptJson(string json) =>
        ScriptJsonNormalizer.Parse(json, JsonOptions);

    private async Task<(ScriptOutputDto? Script, string Provider)> GenerateChunkedNarrationAsync(
        VideoProjectContext ctx,
        ScriptProductionPlanDto plan,
        int wordTarget,
        bool useScriptPromptOnly,
        CancellationToken ct)
    {
        var chunkTargets = LongScriptNarrationPlanner.BuildChunkWordTargets(wordTarget);
        ScriptOutputDto? script = null;
        var parts = new List<string>();
        var provider = "";

        for (var i = 0; i < chunkTargets.Count; i++)
        {
            var chunkIndex = i + 1;
            var isFirst = i == 0;
            var isLast = i == chunkTargets.Count - 1;
            var previous = parts.Count > 0
                ? LongScriptNarrationPlanner.TailForContext(string.Join("\n\n", parts))
                : null;

            var prompt = ScriptTwoPhaseGenerator.BuildNarrationChunkPrompt(
                ctx, plan, chunkIndex, chunkTargets.Count, chunkTargets[i],
                previous, isFirst, isLast, useScriptPromptOnly);

            var (chunk, chunkProvider) = await GenerateNarrationChunkAsync(prompt, useScriptPromptOnly, null, ct);
            provider = chunkProvider;
            if (chunk is null || string.IsNullOrWhiteSpace(chunk.FullNarrationText))
                continue;

            if (isFirst)
                script = chunk;
            else if (script is not null)
                ScriptTwoPhaseGenerator.MergeNarrationMetadata(script, chunk);

            parts.Add(LongScriptNarrationPlanner.CapChunkText(chunk.FullNarrationText, chunkTargets[i]));
        }

        if (script is null || parts.Count == 0)
            return (null, provider);

        script.FullNarrationText = string.Join("\n\n", parts);
        script.ProductionPlan ??= plan;
        script.ImageSuggestions = [];
        script.VideoSuggestions = [];

        return (script, provider);
    }

    private async Task<(ScriptOutputDto? Script, string Provider)> GenerateNarrationChunkAsync(
        string prompt, bool useScriptPromptOnly, int? maxOutputTokens, CancellationToken ct)
    {
        var (json, provider) = await GenerateScriptJsonAsync(prompt, useScriptPromptOnly, maxOutputTokens, ct);
        var script = TryParseScriptJson(json);
        if (script is not null && !string.IsNullOrWhiteSpace(script.FullNarrationText))
            return (script, provider);

        var (retryJson, retryProvider) = await GenerateScriptJsonAsync(
            prompt + JsonRetrySuffix, useScriptPromptOnly, maxOutputTokens, ct);
        return (TryParseScriptJson(retryJson), retryProvider);
    }

    private static ScriptOutputDto ParseStoredScript(string scriptJson) =>
        ScriptJsonNormalizer.Parse(scriptJson, JsonOptions)
        ?? throw new InvalidOperationException("بيانات السكريبت غير صالحة. أعد توليد السكريبت أو الصق JSON صحيح.");

    public async Task<VideoProjectDto> UpdateProductionPlanAsync(
        Guid projectId, UpdateProductionPlanRequest request, CancellationToken ct = default)
    {
        var project = await db.VideoProjects
            .Include(p => p.Category)
            .Include(p => p.GeneratedImages)
            .FirstAsync(p => p.Id == projectId, ct);

        var workflowPath = await ResolveWorkflowPathAsync(project, request.ProductionWorkflowPath, ct);
        var rules = await settings.GetProductionRulesAsync(ct);
        var plan = ProductionPlanResolver.BuildWordCountPlan(request.TargetWordCount, workflowPath);
        var segmentCount = ResolveImageSegmentCount(
            request.ImageSegmentCount, project.ImageSegmentCount, rules.DefaultImageSegmentCount);
        ScriptGenerationHelper.ApplySceneCountsFromSettings(plan, segmentCount, workflowPath);

        if (!string.IsNullOrWhiteSpace(project.ScriptJson))
        {
            var script = ParseStoredScript(project.ScriptJson);
            ProductionPlanResolver.FinalizePlan(plan, script, rules.MaxVideoDurationSec);
            script.ProductionPlan = plan;
            project.ScriptJson = JsonSerializer.Serialize(script, JsonOptions);
        }

        ProductionPlanResolver.ApplyToProject(project, plan);
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var saved = await db.VideoProjects
            .Include(p => p.Category)
            .Include(p => p.GeneratedImages)
            .AsNoTracking()
            .FirstAsync(p => p.Id == projectId, ct);
        return await VideoProjectMapper.MapWithWorkflowAsync(saved, workflowBuilder, ct);
    }

    public async Task<VideoProjectDto> SaveMotionPromptsAsync(
        Guid projectId, SaveMotionPromptsRequest request, CancellationToken ct = default)
    {
        var project = await db.VideoProjects
            .Include(p => p.GeneratedImages)
            .Include(p => p.Category)
            .FirstAsync(p => p.Id == projectId, ct);

        if (string.IsNullOrWhiteSpace(project.ScriptJson))
            throw new InvalidOperationException("Generate script first.");

        var script = ParseStoredScript(project.ScriptJson!);

        foreach (var item in request.Items)
        {
            var motion = item.UserMotionPrompt?.Trim() ?? "";
            if (string.Equals(item.SegmentType, "video", StringComparison.OrdinalIgnoreCase))
            {
                var video = script.VideoSuggestions.FirstOrDefault(v => v.SortOrder == item.SortOrder);
                if (video is not null)
                    video.UserMotionPrompt = motion;
            }
            else
            {
                var image = script.ImageSuggestions.FirstOrDefault(i => i.SortOrder == item.SortOrder);
                if (image is not null)
                    image.UserMotionPrompt = motion;

                var linked = script.VideoSuggestions.FirstOrDefault(v =>
                    v.SortOrder == item.SortOrder || v.LinkedImageSortOrder == item.SortOrder);
                if (linked is not null)
                    linked.UserMotionPrompt = motion;
            }
        }

        project.ScriptJson = JsonSerializer.Serialize(script, JsonOptions);
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var saved = await db.VideoProjects
            .Include(p => p.Category)
            .Include(p => p.GeneratedImages)
            .AsNoTracking()
            .FirstAsync(p => p.Id == projectId, ct);
        return await VideoProjectMapper.MapWithWorkflowAsync(saved, workflowBuilder, ct);
    }

    private static int ResolveImageSegmentCount(int requestCount, int projectCount, int settingsDefault)
    {
        if (requestCount > 0)
            return ScriptGenerationHelper.ClampSegmentCount(requestCount);
        if (projectCount > 0)
            return ScriptGenerationHelper.ClampSegmentCount(projectCount);
        return ScriptGenerationHelper.ClampSegmentCount(settingsDefault);
    }

    private async Task<string> ResolveWorkflowPathAsync(
        VideoProject project, string? requestPath, CancellationToken ct)
    {
        var profile = await DeploymentProfileSource.ResolveAsync(settings, ct);

        if (!string.IsNullOrWhiteSpace(requestPath))
            return DeploymentProfileResolver.ResolveProductionWorkflowPath(profile, requestPath.Trim());

        if (!string.IsNullOrWhiteSpace(project.ProductionWorkflowPath))
            return DeploymentProfileResolver.ResolveProductionWorkflowPath(profile, project.ProductionWorkflowPath);

        var configured = await settings.GetOrDefaultAsync(
            SettingKeys.ProductionWorkflowPath,
            nameof(ProductionWorkflowPath.ImageToVideo),
            ct);
        return DeploymentProfileResolver.ResolveProductionWorkflowPath(profile, configured);
    }
}
