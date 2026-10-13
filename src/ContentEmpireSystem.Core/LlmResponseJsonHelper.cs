using System.Text.RegularExpressions;

namespace ContentEmpireSystem.Core;

public static class LlmResponseJsonHelper
{
    private static readonly Regex ThinkBlockRegex = new(
        @"<(?:think|redacted_reasoning)(?:\s+[^>]*)?>[\s\S]*?</(?:think|redacted_reasoning)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string StripThinkingBlocks(string text) =>
        string.IsNullOrWhiteSpace(text) ? "" : ThinkBlockRegex.Replace(text, "").Trim();
}
