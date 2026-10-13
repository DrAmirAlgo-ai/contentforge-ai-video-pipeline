using ContentEmpireSystem.Core;

namespace ContentEmpireSystem.Core.Entities;

public class GeneratedImage
{
    public int Id { get; set; }
    public Guid VideoProjectId { get; set; }
    public VideoProject VideoProject { get; set; } = null!;

    public string Prompt { get; set; } = string.Empty;
    /// <summary>Per-scene English prompt from script (FLUX scene content only).</summary>
    public string ScenePrompt { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? LocalFilePath { get; set; }
    public int SortOrder { get; set; }
    /// <summary>0 = first visual, 1 = second visual within the same voice segment.</summary>
    public int VisualPartIndex { get; set; }
    public double StartTimeMs { get; set; }
    public double EndTimeMs { get; set; }
    /// <summary>Animation mode: zoom_in, pan_*, oversize_pan_*, ken_burns, random, static</summary>
    public string AnimationMode { get; set; } = "zoom_in";
    public double MotionSpeed { get; set; } = ImageMotionDefaults.MotionSpeed;
    public double MaxZoom { get; set; } = ImageMotionDefaults.MaxZoom;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
