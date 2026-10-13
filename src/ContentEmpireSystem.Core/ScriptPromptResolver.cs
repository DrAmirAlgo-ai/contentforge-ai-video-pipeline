namespace ContentEmpireSystem.Core;

/// <summary>Resolves script-prompt-only mode for script generation.</summary>
public static class ScriptPromptResolver
{
    public static bool ResolveScriptPromptOnlyMode(bool requested, int? categoryId)
    {
        if (requested)
            return true;

        return !categoryId.HasValue;
    }

    public static bool UsesCustomScriptSystemPrompt(bool scriptPromptOnly) => scriptPromptOnly;
}
