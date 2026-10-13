namespace ContentEmpireSystem.Core;

public static class ScriptProviderNames
{
    public const string Ollama = "Ollama";
    public const string LmStudio = "LMStudio";
    public const string OpenAi = "OpenAI";
    public const string Gemini = "Gemini";
    public const string Auto = "Auto";

    public static string? Normalize(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return null;

        return provider.Trim().ToLowerInvariant() switch
        {
            "ollama" => Ollama,
            "lmstudio" or "lm_studio" or "lm studio" or "qwen" => LmStudio,
            "openai" => OpenAi,
            "gemini" => Gemini,
            "auto" => Auto,
            _ => provider.Trim()
        };
    }

    public static string DisplayLabel(string? provider) =>
        Normalize(provider) switch
        {
            LmStudio => "LM Studio (Qwen)",
            Gemini => "Gemini",
            OpenAi => "OpenAI",
            Ollama => "Ollama",
            Auto => "Auto",
            null => "AI",
            var p => p
        };
}
