using System.Text.Json;
using ContentEmpireSystem.Core.DTOs;

namespace ContentEmpireSystem.Core;

/// <summary>Stores LLM script output verbatim — no Hook/CTA/SEO field splitting or enrichment.</summary>
public static class PromptOnlyScriptParser
{
    public static ScriptOutputDto? Parse(string raw, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        options ??= LlmJsonParser.DefaultOptions;
        var text = raw.Trim();

        var fromJson = TryReadFullNarrationFromJson(text, options);
        if (!string.IsNullOrWhiteSpace(fromJson))
            text = fromJson;

        if (string.IsNullOrWhiteSpace(text))
            return null;

        return new ScriptOutputDto { FullNarrationText = text };
    }

    private static string? TryReadFullNarrationFromJson(string raw, JsonSerializerOptions options)
    {
        if (!raw.TrimStart().StartsWith('{'))
            return null;

        foreach (var candidate in LlmJsonParser.AllRepairCandidates(raw))
        {
            var script = LlmJsonParser.Deserialize<ScriptOutputDto>(candidate, options);
            if (!string.IsNullOrWhiteSpace(script?.FullNarrationText))
                return script.FullNarrationText.Trim();
        }

        return null;
    }
}
