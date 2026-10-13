using ContentEmpireSystem.Core;

/// <summary>
/// Configuration for ComfyUI generation settings, especially for low-VRAM laptops.
/// </summary>
public static class LocalComfyGenerationSettings
{
    /// <summary>
    /// Minimum free VRAM (in MB) before allowing ComfyUI to start.
    /// Used to prevent crashes on low-VRAM systems.
    public const int MinFreeVramMiB = 5800;

    /// <summary>
    /// Minimum free VRAM before starting Flux generation.
    /// </summary>
    public const int MinFreeVramFluxMiB = 4200;

    /// <summary>
    /// Maximum number of steps for WAN video generation.
    /// </summary>
    public const int MaxWanSteps = 50;

    /// <summary>
    /// Maximum frame count for WAN video generation.
    /// </summary>
    public const int MaxWanFrames = 50;

    /// <summary>
    /// Maximum width for WAN video generation.
    /// </summary>
    public const int MaxWanWidth = 720;

    /// <summary>
    /// Maximum height for WAN video generation.
    /// </summary>
    public const int MaxWanHeight = 720;

    /// <summary>
    /// Output FPS for saved videos.
    /// </summary>
    public const double DefaultFps = 24.0;

    /// <summary>
    /// Denoise strength (default: 1.0).
    /// </summary>
    public const double DefaultDenoise = 1.0;

    /// <summary>
    /// Whether to apply low VRAM limits (clamping) for WAN settings - disabled by default, allow full control from dashboard.
    /// </summary>
    public const string LocalWanApplyLowVramLimits = "Local.ComfyUI.Wan.ApplyLowVramLimits";

    /// <summary>
    /// Default WAN negative prompt (words to avoid writing in videos).
    /// </summary>
    public const string DefaultWanNegativePrompt = "text, letters, captions, subtitles, watermark, signature, logo, writing, gibberish text, blurry, distorted";

    /// <summary>
    /// Default WAN GGUF UNET filename (models/unet search path).
    /// </summary>
    public const string DefaultWanUnetName = "wan2.2-video-vae-unet-ds128-cogview3-2-bf16-vit-fp16.safetensors";

    /// <summary>
    /// Default WAN VAE filename (models/vae search path).
    /// </summary>
    public const string DefaultWanVaeName = "wan2.2-video-vae-fp16.safetensors";
}
