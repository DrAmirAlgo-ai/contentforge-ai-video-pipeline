using System.Text.Json.Serialization;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.Enums;

namespace ContentEmpireSystem.Core.DTOs;

public class VideoProjectDto
{
    public Guid Id { get; set; }
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public VideoProjectStatus Status { get; set; }
    public string? RejectionReason { get; set; }
    public string? CurrentIdea { get; set; }
    public string? ConfirmedIdea { get; set; }
    public ScriptOutputDto? Script { get; set; }
    public string? VideoFilePath { get; set; }
    public string? AiProviderUsed { get; set; }
    public string? TtsProviderUsed { get; set; }
    public string? ImageProviderUsed { get; set; }
    public string? VideoProviderUsed { get; set; }
    public string ProductionWorkflowPath { get; set; } = "ImageToVideo";
    public bool HasManualAudio { get; set; }
    public bool HasAudio { get; set; }
    public bool HasManualImages { get; set; }
    public bool HasManualVideos { get; set; }
    public bool HasVoiceCode { get; set; }
    public string ContentLanguage { get; set; } = "ar";
    public int TargetWordCount { get; set; } = ScriptGenerationHelper.DefaultWordCount;
    public int TargetDurationSec { get; set; } = ScriptGenerationHelper.DefaultDurationSec;
    public int ImageSegmentCount { get; set; } = ScriptGenerationHelper.DefaultImageSegmentCount;
    public int VideoSegmentCount { get; set; } = ScriptGenerationHelper.DefaultVideoSegmentCount;
    public List<GeneratedImageDto> Images { get; set; } = [];
    public ProjectWorkflowStateDto? Workflow { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ProjectWorkflowStateDto
{
    public string WorkflowPath { get; set; } = "ImageToVideo";
    public bool IsTextToVideo { get; set; }
    public ProjectWorkflowStep CurrentStep { get; set; }
    public string NextActionAr { get; set; } = string.Empty;
    public List<ProjectWorkflowStep> CompletedSteps { get; set; } = [];
    public List<string> Blockers { get; set; } = [];
    public bool CanProduce { get; set; }
    public bool VideoGenerationSupported { get; set; } = true;
    public bool IsVoiceCode { get; set; }
    public int SegmentCount { get; set; }
    public int RequiredSegments { get; set; }
    public int StaticImageCount { get; set; }
    public int VideoClipCount { get; set; }
    public bool RequireWanAnimation { get; set; }
    public bool IsBusy { get; set; }
    public string? ActiveOperation { get; set; }
    /// <summary>Operations the client may invoke now (matches ProjectWorkflowGuard).</summary>
    public List<string> AllowedOperations { get; set; } = [];
}

public class GeneratedImageDto
{
    public int Id { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string ScenePrompt { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? LocalFilePath { get; set; }
    public int SortOrder { get; set; }
    public int VisualPartIndex { get; set; }
    public bool IsVideoClip { get; set; }
    public double DurationSec { get; set; }
    public string AnimationMode { get; set; } = "zoom_in";
    public double MotionSpeed { get; set; } = 0.002;
    public double MaxZoom { get; set; } = 1.3;
}

public class GenerateImagesRequest
{
    public int Count { get; set; } = 5;
    public bool ReplaceExisting { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }
}

public class GenerateVideosRequest
{
    public int Count { get; set; } = 5;
    public bool ReplaceExisting { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }
}

public class AnimateImagesRequest
{
    public bool ReplaceClips { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }
}

public class LocalAiHealthDto
{
    public string DeploymentProfile { get; set; } = DeploymentProfileNames.Local;
    public string EndpointMode { get; set; } = "Local";
    public string EffectiveScriptProvider { get; set; } = ScriptProviderNames.LmStudio;
    public string EffectiveImageProvider { get; set; } = ImageProviderNames.ComfyFlux;
    public string EffectiveVideoProvider { get; set; } = "WanTI2V";
    public bool LmStudioReachable { get; set; }
    public bool ComfyUiReachable { get; set; }
    public bool GeminiConfigured { get; set; }
    public bool HuggingFaceConfigured { get; set; }
    public bool ScriptProviderReady { get; set; }
    public bool ImageProviderReady { get; set; }
    public bool VideoProviderReady { get; set; }
    public bool VideoGenerationSupported { get; set; } = true;
    public bool OverallReady { get; set; }
    public string? LmStudioUrl { get; set; }
    public string? ComfyUiUrl { get; set; }
    public string? Message { get; set; }
}

public class RegenerateImageRequest
{
    [JsonPropertyName("provider")]
    public string? Provider { get; set; }
}

public class SetImageAnimationRequest
{
    [JsonPropertyName("animationMode")]
    public string AnimationMode { get; set; } = "zoom_in";
    [JsonPropertyName("motionSpeed")]
    public double MotionSpeed { get; set; } = 0.002;
    [JsonPropertyName("maxZoom")]
    public double MaxZoom { get; set; } = 1.3;
}

public class BulkImageAnimationRequest
{
    public List<ImageAnimationItem> Animations { get; set; } = [];
}

public class ImageAnimationItem
{
    [JsonPropertyName("imageId")]
    public int ImageId { get; set; }
    [JsonPropertyName("animationMode")]
    public string AnimationMode { get; set; } = "zoom_in";
    [JsonPropertyName("motionSpeed")]
    public double MotionSpeed { get; set; } = 0.002;
    [JsonPropertyName("maxZoom")]
    public double MaxZoom { get; set; } = 1.3;
}

public class GenerateAudioRequest
{
    public string? VoiceId { get; set; }
    public string? EdgeVoice { get; set; }
    public string? Language { get; set; }
}

public class SetContentLanguageRequest
{
    public string Language { get; set; } = "ar";
}

public class GenerateIdeaRequest
{
    public int? CategoryId { get; set; }
    public Guid? ProjectId { get; set; }
    public string? Language { get; set; }
    public bool UseScriptPromptOnly { get; set; }
}

public class ConfirmIdeaRequest
{
    /// <summary>Existing project, or null/empty to create one when skipping idea generation.</summary>
    public Guid? ProjectId { get; set; }
    public string? Language { get; set; }
}

public class GenerateScriptRequest
{
    public Guid ProjectId { get; set; }
    public string? Language { get; set; }
    public bool UseScriptPromptOnly { get; set; }

    /// <summary>Target narration word count (75–10000).</summary>
    public int TargetWordCount { get; set; } = ScriptGenerationHelper.DefaultWordCount;

    /// <summary>Image/video scene count (1–160). 0 = use project or settings default.</summary>
    public int ImageSegmentCount { get; set; }

    public string? ProductionWorkflowPath { get; set; }
}

public class UpdateProductionPlanRequest
{
    public int TargetWordCount { get; set; } = ScriptGenerationHelper.DefaultWordCount;
    public int ImageSegmentCount { get; set; }
    public string? ProductionWorkflowPath { get; set; }
}

public class SaveMotionPromptsRequest
{
    public List<MotionPromptItem> Items { get; set; } = [];
}

public class MotionPromptItem
{
    public int SortOrder { get; set; }
    public string UserMotionPrompt { get; set; } = string.Empty;
    /// <summary>image | video</summary>
    public string SegmentType { get; set; } = "image";
}

public class ManualIdeaRequest
{
    public Guid? ProjectId { get; set; }
    public int? CategoryId { get; set; }
    public string Idea { get; set; } = string.Empty;
}

public class ConfirmManualIdeaRequest
{
    public Guid? ProjectId { get; set; }
    public int? CategoryId { get; set; }
    public string Idea { get; set; } = string.Empty;
    public string? Language { get; set; }
}

public class VoiceCodeScriptRequest
{
    public Guid ProjectId { get; set; }
    public string ScriptText { get; set; } = string.Empty;
}

public class OrchestratePipelineRequest
{
    public bool IncludeImages { get; set; } = true;
    public bool IncludeAudio { get; set; } = true;
    public bool IncludeProduction { get; set; } = false;
    public string? ImageProvider { get; set; }
}

public class ContentCategoryDto
{
    public int Id { get; set; }
    public ContentCategoryType CategoryType { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
