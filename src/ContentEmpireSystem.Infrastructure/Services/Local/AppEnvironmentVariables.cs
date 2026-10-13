namespace ContentEmpireSystem.Infrastructure.Services.Local;

/// <summary>Reads optional defaults from environment (.env) when DB settings are empty.</summary>
internal static class AppEnvironmentVariables
{
    public static string? GetLmStudioModel() =>
        FirstNonEmpty(
            Environment.GetEnvironmentVariable("LM_STUDIO_MODEL"),
            Environment.GetEnvironmentVariable("Api__LMStudioModel"));

    public static string? GetDeploymentProfile() =>
        FirstNonEmpty(
            Environment.GetEnvironmentVariable("DEPLOYMENT_PROFILE"),
            Environment.GetEnvironmentVariable("Api__DeploymentProfile"));

    public static string? GetGeminiApiKey() =>
        FirstNonEmpty(
            Environment.GetEnvironmentVariable("GEMINI_API_KEY"),
            Environment.GetEnvironmentVariable("GOOGLE_API_KEY"),
            Environment.GetEnvironmentVariable("Api__Gemini"));

    public static string? GetHuggingFaceApiKey() =>
        FirstNonEmpty(
            Environment.GetEnvironmentVariable("HUGGINGFACE_API_KEY"),
            Environment.GetEnvironmentVariable("HF_API_KEY"),
            Environment.GetEnvironmentVariable("Api__HuggingFace"));

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }

        return null;
    }
}
