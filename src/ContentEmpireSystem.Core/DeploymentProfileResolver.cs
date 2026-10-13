namespace ContentEmpireSystem.Core;

public static class DeploymentProfileResolver
{
    public static string Resolve(string? envOrSetting) =>
        DeploymentProfileNames.Normalize(envOrSetting) ?? DeploymentProfileNames.Local;

    public static bool UsesCloudApis(string profile) =>
        profile == DeploymentProfileNames.Online;

    public static bool ExpectsLocalLmStudio(string profile) =>
        profile is DeploymentProfileNames.Local;

    public static string DefaultImageProvider(string profile) => profile switch
    {
        DeploymentProfileNames.Online => ImageProviderNames.HuggingFace,
        _ => ImageProviderNames.ComfyFlux
    };

    public static string DefaultScriptProvider(string profile) => profile switch
    {
        DeploymentProfileNames.Online => ScriptProviderNames.Gemini,
        _ => ScriptProviderNames.LmStudio
    };

    /// <summary>Maps dashboard deployment profile to the script LLM actually used.</summary>
    public static string ResolveScriptProvider(string profile, string? configured)
    {
        var normalized = ScriptProviderNames.Normalize(configured);

        if (UsesCloudApis(profile))
        {
            return normalized switch
            {
                ScriptProviderNames.Gemini or ScriptProviderNames.OpenAi or ScriptProviderNames.Auto => normalized!,
                _ => DefaultScriptProvider(profile)
            };
        }

        return normalized switch
        {
            ScriptProviderNames.LmStudio or ScriptProviderNames.Ollama or ScriptProviderNames.Auto => normalized!,
            _ => DefaultScriptProvider(profile)
        };
    }

    public static bool SupportsVideoGeneration(string profile) => !UsesCloudApis(profile);

    /// <summary>Online profile forces ImageToVideo — T2V/Wan require local GPU.</summary>
    public static string ResolveProductionWorkflowPath(string profile, string? configured)
    {
        var path = ProductionPlanResolver.NormalizeWorkflowPath(configured);
        if (!SupportsVideoGeneration(profile) && ProductionPlanResolver.IsTextToVideo(path))
            return "ImageToVideo";
        return path;
    }

    public static string DefaultVideoProvider(string profile) => profile switch
    {
        _ => VideoProviderNames.WanTi2v
    };

    /// <summary>Maps deployment profile to the image provider actually used.</summary>
    public static string ResolveImageProvider(string profile, string? configured)
    {
        var normalized = ImageProviderNames.Normalize(configured);

        if (UsesCloudApis(profile))
        {
            return normalized switch
            {
                ImageProviderNames.HuggingFace => ImageProviderNames.HuggingFace,
                _ => DefaultImageProvider(profile)
            };
        }

        return normalized switch
        {
            ImageProviderNames.ComfyFlux => ImageProviderNames.ComfyFlux,
            _ => DefaultImageProvider(profile)
        };
    }
}
