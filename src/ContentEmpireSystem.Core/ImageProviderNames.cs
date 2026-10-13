namespace ContentEmpireSystem.Core;

public static class ImageProviderNames
{
    public const string HuggingFace = "HuggingFace";
    public const string ComfyFlux = "ComfyFlux";
    public const string Unsloth = "Unsloth";

    public static string? Normalize(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return null;

        return provider.Trim().ToLowerInvariant() switch
        {
            "huggingface" or "hf" => HuggingFace,
            "comfyflux" or "comfy" or "flux" or "local" => ComfyFlux,
            "unsloth" => Unsloth,
            _ => provider.Trim()
        };
    }

    public static bool IsSupported(string? provider) =>
        provider is HuggingFace or ComfyFlux or Unsloth;

    public static bool IsLocal(string? provider) =>
        provider is ComfyFlux;
}
