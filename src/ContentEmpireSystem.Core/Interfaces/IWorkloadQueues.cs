namespace ContentEmpireSystem.Core.Interfaces;

public enum ProductionEnqueueResult
{
    Queued,
    AlreadyQueued,
    AlreadyProcessing,
    QueueFull
}

public interface IProductionQueue
{
    Task<ProductionEnqueueResult> EnqueueAsync(Guid projectId, CancellationToken ct = default);
    bool IsActive(Guid projectId);
}

public sealed record ImageGenerationJob(
    Guid ProjectId,
    int Count,
    bool ReplaceExisting,
    string? Provider);

public enum ImageGenerationEnqueueResult
{
    Queued,
    AlreadyProcessing,
    QueueFull
}

public interface IImageGenerationQueue
{
    Task<ImageGenerationEnqueueResult> EnqueueAsync(ImageGenerationJob job, CancellationToken ct = default);
    bool IsActive(Guid projectId);
}

public enum VideoGenerationMode
{
    AnimateImages,
    TextToVideo
}

public sealed record VideoGenerationJob(
    Guid ProjectId,
    VideoGenerationMode Mode,
    int Count,
    bool ReplaceExisting,
    string? Provider);

public enum VideoGenerationEnqueueResult
{
    Queued,
    AlreadyProcessing,
    QueueFull
}

public interface IVideoGenerationQueue
{
    Task<VideoGenerationEnqueueResult> EnqueueAsync(VideoGenerationJob job, CancellationToken ct = default);
    bool IsActive(Guid projectId);
}

public enum AiLlmJobKind
{
    IdeaGeneration,
    ScriptGeneration
}

public sealed record AiLlmGenerationJob(
    Guid? ProjectId,
    AiLlmJobKind Kind,
    int? CategoryId,
    string? Language,
    bool UseScriptPromptOnly,
    int TargetWordCount,
    int ImageSegmentCount,
    string? ProductionWorkflowPath);

public enum AiLlmEnqueueResult
{
    Queued,
    AlreadyProcessing,
    QueueFull
}

public interface IAiLlmGenerationQueue
{
    Task<AiLlmEnqueueResult> EnqueueAsync(AiLlmGenerationJob job, CancellationToken ct = default);
    bool IsActive(Guid projectId);
}

public sealed record PipelineOrchestrationJob(
    Guid ProjectId,
    bool IncludeImages,
    bool IncludeAudio,
    bool IncludeProduction,
    string? ImageProvider);

public enum PipelineOrchestrationEnqueueResult
{
    Queued,
    AlreadyProcessing,
    QueueFull
}

public interface IPipelineOrchestrationQueue
{
    Task<PipelineOrchestrationEnqueueResult> EnqueueAsync(PipelineOrchestrationJob job, CancellationToken ct = default);
    bool IsActive(Guid projectId);
}
