using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.Enums;

namespace ContentEmpireSystem.Core.Entities;

public class VideoProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? OwnerUserId { get; set; }
    public int? CategoryId { get; set; }
    public ContentCategory? Category { get; set; }

    public VideoProjectStatus Status { get; set; } = VideoProjectStatus.Draft;

    public string? CurrentIdea { get; set; }
    public string? ConfirmedIdea { get; set; }
    public string? ScriptJson { get; set; }
    public string? RejectionReason { get; set; }

    public string? AudioFilePath { get; set; }
    public string? WordTimestampsJson { get; set; }
    public string? VideoFilePath { get; set; }
    public string? AssSubtitlePath { get; set; }

    public string? AiProviderUsed { get; set; }
    public string? TtsProviderUsed { get; set; }
    public string? ImageProviderUsed { get; set; }
    public string? VideoProviderUsed { get; set; }

    /// <summary>ImageToVideo | TextToVideo — local production path.</summary>
    public string ProductionWorkflowPath { get; set; } = "ImageToVideo";

    /// <summary>ar | en | fr | tr — drives idea, script, and default TTS voice.</summary>
    public string ContentLanguage { get; set; } = "ar";

    public int TargetWordCount { get; set; } = ScriptGenerationHelper.DefaultWordCount;
    /// <summary>Derived from word count — kept for audio timing and legacy scripts.</summary>
    public int TargetDurationSec { get; set; } = ScriptGenerationHelper.DefaultDurationSec;
    public int ImageSegmentCount { get; set; } = ScriptGenerationHelper.DefaultImageSegmentCount;
    public int VideoSegmentCount { get; set; } = ScriptGenerationHelper.DefaultVideoSegmentCount;

    /// <summary>حقل ترحيل قديم — لا يُستخدم في منطق الأعمال الحالي.</summary>
    [Obsolete("حقل ترحيل قديم — يُحفظ في قاعدة البيانات فقط.")]
    public int ImagesPer15Sec { get; set; } = 1;
    [Obsolete("حقل ترحيل قديم — يُحفظ في قاعدة البيانات فقط.")]
    public int VideosPer15Sec { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<GeneratedImage> GeneratedImages { get; set; } = [];
}
