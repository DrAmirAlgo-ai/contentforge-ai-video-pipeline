namespace ContentEmpireSystem.Core;

/// <summary>Combines global FLUX style/identity prompt with per-scene script prompts.</summary>
public static class FluxPromptComposer
{
    public const string SceneSectionHeader = "Scene subject and action:";

    /// <summary>Prompt sent to image models: global style + English scene content only.</summary>
    public static string ComposeForGeneration(string? stylePrompt, string? scenePrompt)
    {
        var style = stylePrompt?.Trim();
        var scene = scenePrompt?.Trim();

        if (string.IsNullOrEmpty(style))
            return scene ?? string.Empty;
        if (string.IsNullOrEmpty(scene))
            return style;

        return $"{style}\n\n{SceneSectionHeader} {scene}";
    }

    /// <summary>Extract scene text from a stored combined prompt (for regenerate after style edits).</summary>
    public static string ExtractScenePrompt(string? storedPrompt, string? currentStylePrompt)
    {
        if (string.IsNullOrWhiteSpace(storedPrompt))
            return string.Empty;

        var text = storedPrompt.Trim();
        var markerIndex = text.IndexOf(SceneSectionHeader, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            var after = text[(markerIndex + SceneSectionHeader.Length)..].TrimStart(':', ' ');
            return after.Trim();
        }

        var style = currentStylePrompt?.Trim();
        if (!string.IsNullOrEmpty(style) && text.StartsWith(style, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = text[style.Length..].TrimStart('\r', '\n', ' ');
            return remainder.Trim();
        }

        return text;
    }
}
