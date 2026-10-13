using ContentEmpireSystem.Core.Enums;

namespace ContentEmpireSystem.Core;

public static class ContentLanguageCodes
{
    public const string Arabic = "ar";
    public const string English = "en";
    public const string French = "fr";
    public const string Turkish = "tr";
}

public static class ContentLanguageHelper
{
    public static readonly IReadOnlyList<(string Code, string LabelEn, string LabelAr)> Supported =
    [
        (ContentLanguageCodes.Arabic, "Arabic", "العربية"),
        (ContentLanguageCodes.English, "English", "English"),
        (ContentLanguageCodes.French, "French", "Français"),
        (ContentLanguageCodes.Turkish, "Turkish", "Türkçe"),
    ];

    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return ContentLanguageCodes.Arabic;

        var trimmed = code.Trim().ToLowerInvariant();
        return Supported.Any(l => l.Code == trimmed) ? trimmed : ContentLanguageCodes.Arabic;
    }

    public static string GetDisplayName(string? code, bool arabicUi = false)
    {
        var normalized = Normalize(code);
        var match = Supported.First(l => l.Code == normalized);
        return arabicUi ? match.LabelAr : match.LabelEn;
    }

    public static string GetDefaultEdgeVoice(string? code) => Normalize(code) switch
    {
        ContentLanguageCodes.English => "en-US-GuyNeural",
        ContentLanguageCodes.French => "fr-FR-HenriNeural",
        ContentLanguageCodes.Turkish => "tr-TR-AhmetNeural",
        _ => "ar-EG-ShakirNeural",
    };

    public static string BuildIdeaPromptBlock(string? code)
    {
        var lang = GetDisplayName(code);
        var normalized = Normalize(code);
        return $"""
            {BuildOutputLanguageDirective(code)}
            CRITICAL LANGUAGE RULE (code: {normalized}):
            The JSON fields "idea", "angle", and "trendHook" MUST be written entirely in {lang}.
            Do not mix languages. Do not output English unless the required language is English.
            """;
    }

    public static string BuildScriptPromptBlock(string? code)
    {
        var lang = GetDisplayName(code);
        var normalized = Normalize(code);
        return $"""
            {BuildOutputLanguageDirective(code)}
            CRITICAL LANGUAGE RULE (code: {normalized}):
            Write fullNarrationText, primaryHook, extendedHook, conclusionAndCta, viewerBenefit, and all script body text entirely in {lang}.
            SEO title, description, and hashtags must be in {lang}.
            imageSuggestions "prompt" may be English for image models; "sceneDescription" must be in {lang}.
            Do not mix languages.
            """;
    }

    public static string BuildOutputLanguageDirective(string? code)
    {
        var normalized = Normalize(code);
        var label = GetDisplayName(code);

        if (normalized == ContentLanguageCodes.Arabic)
        {
            return """
                OUTPUT LANGUAGE: Arabic (ar)
                Write the entire response in Arabic only. Do not mix languages.
                Use the exact dialect, vocabulary, and tone specified in your system instructions (e.g. Egyptian colloquial).
                Do NOT default to Modern Standard Arabic (فصحى) unless your system instructions explicitly require it.
                """;
        }

        return $"""
            OUTPUT LANGUAGE: {label} ({normalized})
            Write the entire response in {label} only. Do not mix languages.
            """;
    }

    public static async Task<string> ResolveForProjectAsync(
        string? projectLanguage,
        string? requestOverride,
        Func<string, string, CancellationToken, Task<string>> getSettingAsync,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requestOverride))
            return Normalize(requestOverride);

        if (!string.IsNullOrWhiteSpace(projectLanguage))
            return Normalize(projectLanguage);

        var fromSettings = await getSettingAsync(SettingKeys.DefaultContentLanguage, ContentLanguageCodes.Arabic, ct);
        return Normalize(fromSettings);
    }
}
