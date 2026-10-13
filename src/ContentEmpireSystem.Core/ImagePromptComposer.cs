using ContentEmpireSystem.Core.DTOs;

namespace ContentEmpireSystem.Core;

/// <summary>Builds FLUX/Wan prompts from structured scene fields and global style.</summary>
public static class ImagePromptComposer
{
    public const string SceneSectionHeader = "Scene subject and action:";

    public static string ComposeSceneContent(ImageSuggestionDto suggestion)
    {
        var subject = suggestion.SubjectDescription?.Trim();
        var action = suggestion.ActionDescription?.Trim();
        var camera = suggestion.CameraAngle?.Trim();

        if (!string.IsNullOrEmpty(suggestion.Prompt?.Trim()))
            return suggestion.Prompt.Trim();

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(subject))
            parts.Add($"Subject: {subject}");
        if (!string.IsNullOrEmpty(action))
            parts.Add($"Action: {action}");
        if (!string.IsNullOrEmpty(camera))
            parts.Add($"Camera: {camera}");

        parts.Add("No text, letters, words, or captions in the image.");

        return parts.Count > 0
            ? string.Join(". ", parts)
            : suggestion.SceneDescription?.Trim() ?? string.Empty;
    }

    /// <summary>Prompt sent to image models: global style + structured scene content.</summary>
    public static string ComposeForGeneration(string? stylePrompt, ImageSuggestionDto suggestion) =>
        FluxPromptComposer.ComposeForGeneration(stylePrompt, ComposeSceneContent(suggestion));

    /// <summary>Wan: global style + scene motion + editable positive rules (negative is sent separately to ComfyUI).</summary>
    public static string ComposeForWan(string? wanStylePrompt, string? userMotionPrompt, string? globalPositiveRules = null)
    {
        var parts = new List<string>();
        var style = wanStylePrompt?.Trim();
        var motion = StripEmbeddedNegative(userMotionPrompt);
        var rules = globalPositiveRules?.Trim();

        if (!string.IsNullOrEmpty(style))
            parts.Add(style);
        if (!string.IsNullOrEmpty(motion))
            parts.Add($"Motion and camera movement: {motion}");
        if (!string.IsNullOrEmpty(rules))
            parts.Add(rules);

        return string.Join("\n\n", parts);
    }

    public static string StripEmbeddedNegative(string? userMotionPrompt)
    {
        var motion = userMotionPrompt?.Trim() ?? "";
        if (motion.Length == 0)
            return "";

        var marker = "### negative:";
        var idx = motion.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? motion[..idx].Trim() : motion;
    }

    public static string ExtractScenePrompt(string? storedPrompt, string? currentStylePrompt) =>
        FluxPromptComposer.ExtractScenePrompt(storedPrompt, currentStylePrompt);
}
