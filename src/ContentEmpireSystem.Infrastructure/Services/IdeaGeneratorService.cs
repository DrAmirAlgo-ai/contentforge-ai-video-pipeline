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
public class IdeaGeneratorService(
    ApplicationDbContext db,
    AiChatService aiChat,
    AiProviderResolver providerResolver,
    ISystemSettingsService settings,
    SystemActivityService activity,
    IProjectWorkflowBuilder workflowBuilder,
    ICurrentUserAccessor currentUser) : IIdeaGeneratorService
{
    public async Task<VideoProjectDto> GenerateIdeaAsync(
        int? categoryId, Guid? existingProjectId, string? language = null, bool useScriptPromptOnly = false, CancellationToken ct = default)
    {
        if (categoryId.HasValue)
            useScriptPromptOnly = false;

        VideoProject project;

        if (existingProjectId.HasValue)
        {
            project = await db.VideoProjects
                .Include(p => p.Category)
                .Include(p => p.GeneratedImages)
                .FirstAsync(p => p.Id == existingProjectId.Value, ct);
        }
        else
        {
            project = new VideoProject { OwnerUserId = currentUser.RequireUserId() };
            db.VideoProjects.Add(project);
        }

        ContentCategory? category = null;
        if (!useScriptPromptOnly && !categoryId.HasValue)
            throw new InvalidOperationException("Select a content niche, or choose Script Prompt Only mode.");

        if (!useScriptPromptOnly)
        {
            project.CategoryId = categoryId;
            category = await db.ContentCategories.FirstAsync(c => c.Id == categoryId!.Value, ct);
        }
        else
        {
            project.CategoryId = null;
        }

        project.ContentLanguage = await ResolveLanguageAsync(project, language, ct);

        var scriptConfig = await providerResolver.ResolveScriptProviderAsync(ct);
        activity.Start(project.Id, "توليد الفكرة", ScriptProviderNames.DisplayLabel(scriptConfig.Provider));
        try
        {
            string json;
            string provider;
            if (useScriptPromptOnly)
            {
                var promptDrivenIdeaRequest = $"""
                    {ContentLanguageHelper.BuildIdeaPromptBlock(project.ContentLanguage)}
                    Script-only mode: derive ONE clear content idea strictly from Script System Prompt instructions.
                    Do not inject category or niche unless explicitly stated in Script System Prompt.
                    Return JSON only with this structure:
                    "idea": "..."
                    """;
                (json, provider) = await aiChat.GenerateJsonAsync(
                    promptDrivenIdeaRequest,
                    SettingKeys.ScriptSystemPromptInstructions,
                    SettingKeys.ScriptSystemPromptJsonSchema,
                    SettingKeys.ScriptSystemPrompt,
                    ct: ct);
                var derivedIdea = ExtractIdeaOrFallback(json);
                project.CurrentIdea = derivedIdea;
                project.ConfirmedIdea = derivedIdea;
                project.Status = VideoProjectStatus.IdeaConfirmed;
            }
            else
            {
                var userPrompt = $"""
                    {ContentLanguageHelper.BuildIdeaPromptBlock(project.ContentLanguage)}
                    Topic category: {category!.NameEn} ({category.NameAr})
                    Category description: {category.Description}
                    Task: generate one video idea as JSON.
                    """;

                // Local 9B models occasionally return an empty/weak reply. Retry a few
                // times until we get a real idea before giving up.
                var idea = "Script Prompt Only";
                provider = "";
                json = "";
                for (var attempt = 1; attempt <= 3; attempt++)
                {
                    (json, provider) = await aiChat.GenerateJsonAsync(
                        userPrompt,
                        SettingKeys.IdeaGenerationPromptInstructions,
                        SettingKeys.IdeaGenerationPromptJsonSchema,
                        SettingKeys.IdeaGenerationPrompt,
                        ct: ct);
                    idea = ExtractIdeaText(json);
                    if (!IsWeakIdea(idea))
                        break;
                }

                project.CurrentIdea = idea;
                project.Status = VideoProjectStatus.IdeaGenerated;
            }

            project.AiProviderUsed = provider;
            project.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            project.Category = category;
            activity.Complete(project.Id, $"فكرة جاهزة · {provider}");
        }
        catch (Exception ex)
        {
            activity.Fail(project.Id, ex.Message);
            throw;
        }
        return await VideoProjectMapper.MapWithWorkflowAsync(project, workflowBuilder, ct);
    }

    public async Task<VideoProjectDto> SkipIdeaAsync(Guid projectId, string? language = null, CancellationToken ct = default)
    {
        VideoProject project;
        if (projectId == Guid.Empty)
        {
            project = new VideoProject { OwnerUserId = currentUser.RequireUserId() };
            db.VideoProjects.Add(project);
        }
        else
        {
            project = await db.VideoProjects
                .Include(p => p.Category)
                .Include(p => p.GeneratedImages)
                .FirstAsync(p => p.Id == projectId, ct);
        }

        project.ContentLanguage = await ResolveLanguageAsync(project, language, ct);

        if (string.IsNullOrWhiteSpace(project.CurrentIdea) && string.IsNullOrWhiteSpace(project.ConfirmedIdea))
        {
            project.CurrentIdea = project.Category is not null
                ? $"{project.Category.NameEn} — {project.Category.Description}"
                : "Script Prompt Only";
        }

        project.ConfirmedIdea = project.CurrentIdea ?? project.ConfirmedIdea;
        project.Status = VideoProjectStatus.IdeaConfirmed;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return await VideoProjectMapper.MapWithWorkflowAsync(project, workflowBuilder, ct);
    }

    public async Task<VideoProjectDto> ConfirmIdeaAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await db.VideoProjects
            .Include(p => p.Category)
            .Include(p => p.GeneratedImages)
            .FirstAsync(p => p.Id == projectId, ct);

        if (string.IsNullOrWhiteSpace(project.CurrentIdea))
            throw new InvalidOperationException("No idea to confirm. Generate an idea first.");

        project.ConfirmedIdea = project.CurrentIdea;
        project.Status = VideoProjectStatus.IdeaConfirmed;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return await VideoProjectMapper.MapWithWorkflowAsync(project, workflowBuilder, ct);
    }

    private static string ExtractIdeaText(string json) => ExtractIdeaOrFallback(json);

    private static bool IsWeakIdea(string? idea) =>
        string.IsNullOrWhiteSpace(idea)
        || string.Equals(idea.Trim(), "Script Prompt Only", StringComparison.OrdinalIgnoreCase)
        || idea.Trim().Length < 12;

    private static string ExtractIdeaOrFallback(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "Script Prompt Only";

        // Local models frequently truncate long replies mid-string. Try every repair
        // variant (which includes closing unterminated strings/braces) and use the
        // first candidate that parses and yields an idea.
        foreach (var candidate in LlmJsonParser.AllRepairCandidates(json))
        {
            try
            {
                using var doc = JsonDocument.Parse(candidate);
                var root = doc.RootElement;

                if (TryReadIdea(root, out var idea))
                    return idea!;

                if (root.TryGetProperty("script", out var script) && TryReadIdea(script, out idea))
                    return idea!;

                if (root.TryGetProperty("data", out var data) && TryReadIdea(data, out idea))
                    return idea!;

                // Local models sometimes return valid JSON with an unexpected key name.
                // Fall back to the longest string value anywhere in the object.
                var deepest = FindLongestString(root);
                if (!string.IsNullOrWhiteSpace(deepest))
                    return deepest!;
            }
            catch
            {
                // try next repair variant
            }
        }

        var trimmed = json.Trim();
        if (trimmed.Length > 0 && trimmed.Length < 500 && !trimmed.StartsWith('{'))
            return trimmed;

        return "Script Prompt Only";
    }

    private static string? FindLongestString(JsonElement element)
    {
        string? best = null;
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var s = element.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    best = s;
                break;
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var candidate = FindLongestString(prop.Value);
                    if (candidate is not null && (best is null || candidate.Length > best.Length))
                        best = candidate;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var candidate = FindLongestString(item);
                    if (candidate is not null && (best is null || candidate.Length > best.Length))
                        best = candidate;
                }
                break;
        }
        return best;
    }

    private static bool TryReadIdea(JsonElement root, out string? idea)
    {
        idea = null;
        if (root.TryGetProperty("idea", out var ideaProp))
            idea = ideaProp.GetString();

        if (string.IsNullOrWhiteSpace(idea) && root.TryGetProperty("primaryHook", out var hook))
            idea = hook.GetString();

        if (string.IsNullOrWhiteSpace(idea) && root.TryGetProperty("seo", out var seo)
            && seo.ValueKind == JsonValueKind.Object
            && seo.TryGetProperty("title", out var title))
            idea = title.GetString();

        return !string.IsNullOrWhiteSpace(idea);
    }

    private Task<string> ResolveLanguageAsync(VideoProject project, string? languageOverride, CancellationToken ct) =>
        ContentLanguageHelper.ResolveForProjectAsync(
            project.ContentLanguage,
            languageOverride,
            settings.GetOrDefaultAsync,
            ct);
}
