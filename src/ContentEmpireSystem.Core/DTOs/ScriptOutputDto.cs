namespace ContentEmpireSystem.Core.DTOs;

public class ScriptOutputDto
{
    public string ViewerBenefit { get; set; } = string.Empty;
    public SeoMetadataDto Seo { get; set; } = new();
    public string PrimaryHook { get; set; } = string.Empty;
    public string ExtendedHook { get; set; } = string.Empty;
    public ScriptBodyDto Body { get; set; } = new();
    public string ConclusionAndCta { get; set; } = string.Empty;
    public List<ImageSuggestionDto> ImageSuggestions { get; set; } = [];
    public List<VideoSuggestionDto> VideoSuggestions { get; set; } = [];
    public string FullNarrationText { get; set; } = string.Empty;
    public ScriptProductionPlanDto? ProductionPlan { get; set; }
    public List<VoiceSegmentDto> VoiceSegments { get; set; } = [];
    public string VoiceCodeRawText { get; set; } = string.Empty;
    public bool IsVoiceCode => VoiceSegments.Count >= 3;
}

public class VoiceSegmentDto
{
    public int SortOrder { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Label { get; set; }
    public bool HasMedia { get; set; }
    public bool IsVideoClip { get; set; }
    public int? MediaId { get; set; }
    public List<VoiceVisualPartDto> VisualParts { get; set; } = [];
}

public class VoiceVisualPartDto
{
    public int PartIndex { get; set; }
    public bool HasMedia { get; set; }
    public bool IsVideoClip { get; set; }
    public int? MediaId { get; set; }
}

public class SeoMetadataDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Hashtags { get; set; } = [];
}

public class ScriptBodyDto
{
    public string PartOneProblem { get; set; } = string.Empty;
    public string PartTwoSolution { get; set; } = string.Empty;
}

public class ImageSuggestionDto
{
    public string Prompt { get; set; } = string.Empty;
    public string SceneDescription { get; set; } = string.Empty;
    public string NarrationExcerpt { get; set; } = string.Empty;
    public string SubjectDescription { get; set; } = string.Empty;
    public string ActionDescription { get; set; } = string.Empty;
    public string CameraAngle { get; set; } = string.Empty;
    public string UserMotionPrompt { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public class VideoSuggestionDto
{
    public string Prompt { get; set; } = string.Empty;
    public string SceneDescription { get; set; } = string.Empty;
    public string NarrationExcerpt { get; set; } = string.Empty;
    public string UserMotionPrompt { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public int? LinkedImageSortOrder { get; set; }
}

public class WordTimestampDto
{
    public string Word { get; set; } = string.Empty;
    public double StartMs { get; set; }
    public double EndMs { get; set; }
}
