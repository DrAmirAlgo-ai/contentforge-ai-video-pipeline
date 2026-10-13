namespace ContentEmpireSystem.Core.Enums;

/// <summary>Ordered pipeline steps — first incomplete step is CurrentStep.</summary>
public enum ProjectWorkflowStep
{
    Idea = 1,
    Script = 2,
    FluxImages = 3,
    MotionPrompts = 4,
    WanVideo = 5,
    Audio = 6,
    Approval = 7,
    Produce = 8,
    Done = 9,
    Failed = 10,
    VoiceCodeMedia = 11
}

public enum ProjectOperationKind
{
    IdeaGeneration,
    ScriptGeneration,
    VoiceCodeMediaUpload,
    ImageGeneration,
    VideoAnimation,
    VideoGeneration,
    AudioGeneration,
    Production
}
