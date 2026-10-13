using ContentEmpireSystem.Api.Helpers;
using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ContentEmpireSystem.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ProjectsController(
    IVideoProjectService projectService,
    IIdeaGeneratorService ideaService,
    IScriptGeneratorService scriptService,
    IVoiceCodeService voiceCode,
    IImageGeneratorService imageGenerator,
    ITextToSpeechService ttsService,
    IProductionQueue productionQueue,
    IImageGenerationQueue imageGenerationQueue,
    IVideoGenerationQueue videoGenerationQueue,
    IAiLlmGenerationQueue aiLlmQueue,
    IPipelineOrchestrationQueue pipelineQueue,
    IOutputPathService paths,
    ISystemSettingsService settings,
    IProjectOperationCoordinator operationCoordinator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<VideoProjectDto>>> GetQueue(
        [FromQuery] VideoProjectStatus? status, CancellationToken ct)
        => Ok(await projectService.GetQueueAsync(status, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VideoProjectDto>> GetById(Guid id, CancellationToken ct)
    {
        var project = await projectService.GetByIdAsync(id, ct);
        return project is null ? NotFound() : Ok(project);
    }

    /// <summary>Clears in-memory busy lock after a crash/reboot so the project can resume.</summary>
    [HttpPost("{id:guid}/unlock")]
    public async Task<ActionResult<VideoProjectDto>> UnlockProject(Guid id, CancellationToken ct)
    {
        operationCoordinator.ForceClear(id);
        var project = await projectService.GetByIdAsync(id, ct);
        return project is null ? NotFound() : Ok(project);
    }

    [HttpPost("idea/generate")]
    [RequestTimeout("ai-generate")]
    [EnableRateLimiting("heavy")]
    public async Task<ActionResult<VideoProjectDto>> GenerateIdea(
        [FromBody] GenerateIdeaRequest request, CancellationToken ct)
        => Ok(await ideaService.GenerateIdeaAsync(
            request.CategoryId,
            request.ProjectId,
            request.Language,
            request.UseScriptPromptOnly,
            ct));

    [HttpPost("idea/skip")]
    public async Task<ActionResult<VideoProjectDto>> SkipIdea(
        [FromBody] ConfirmIdeaRequest request, CancellationToken ct)
        => Ok(await ideaService.SkipIdeaAsync(request.ProjectId ?? Guid.Empty, request.Language, ct));

    [HttpPost("idea/confirm")]
    public async Task<ActionResult<VideoProjectDto>> ConfirmIdea(
        [FromBody] ConfirmIdeaRequest request, CancellationToken ct)
    {
        if (request.ProjectId is null || request.ProjectId == Guid.Empty)
            return BadRequest(ApiErrorWriter.ToBody(OperationHints.WorkflowBlocked("معرّف المشروع مطلوب."), HttpContext));

        return Ok(await ideaService.ConfirmIdeaAsync(request.ProjectId!.Value, ct));
    }

    [HttpPost("script/generate")]
    [EnableRateLimiting("heavy")]
    public async Task<IActionResult> GenerateScript(
        [FromBody] GenerateScriptRequest request, CancellationToken ct)
    {
        if (aiLlmQueue.IsActive(request.ProjectId))
            return QueueEnqueueResponses.AlreadyProcessing(HttpContext, request.ProjectId, ProjectOperationKind.ScriptGeneration);

        var job = new AiLlmGenerationJob(
            request.ProjectId,
            AiLlmJobKind.ScriptGeneration,
            null,
            request.Language,
            request.UseScriptPromptOnly,
            request.TargetWordCount,
            request.ImageSegmentCount,
            request.ProductionWorkflowPath);

        return await aiLlmQueue.EnqueueAsync(job, ct) switch
        {
            AiLlmEnqueueResult.Queued => QueueEnqueueResponses.AcceptedJob(HttpContext, request.ProjectId, ProjectOperationKind.ScriptGeneration),
            AiLlmEnqueueResult.AlreadyProcessing => QueueEnqueueResponses.AlreadyProcessingWithError(HttpContext, request.ProjectId, ProjectOperationKind.ScriptGeneration),
            _ => QueueEnqueueResponses.QueueFull(HttpContext, ProjectOperationKind.ScriptGeneration)
        };
    }

    [HttpPost("{id:guid}/orchestrate")]
    [EnableRateLimiting("heavy")]
    public async Task<IActionResult> OrchestratePipeline(
        Guid id, [FromBody] OrchestratePipelineRequest? request, CancellationToken ct)
    {
        if (pipelineQueue.IsActive(id))
            return QueueEnqueueResponses.AlreadyProcessing(HttpContext, id, ProjectOperationKind.Production);

        request ??= new OrchestratePipelineRequest();
        var job = new PipelineOrchestrationJob(
            id,
            request.IncludeImages,
            request.IncludeAudio,
            request.IncludeProduction,
            request.ImageProvider);

        return await pipelineQueue.EnqueueAsync(job, ct) switch
        {
            PipelineOrchestrationEnqueueResult.Queued => QueueEnqueueResponses.AcceptedJob(HttpContext, id, ProjectOperationKind.Production),
            PipelineOrchestrationEnqueueResult.AlreadyProcessing => QueueEnqueueResponses.AlreadyProcessingWithError(HttpContext, id, ProjectOperationKind.Production),
            _ => QueueEnqueueResponses.QueueFull(HttpContext, ProjectOperationKind.Production)
        };
    }

    [HttpPut("{id:guid}/production-plan")]
    public async Task<ActionResult<VideoProjectDto>> UpdateProductionPlan(
        Guid id, [FromBody] UpdateProductionPlanRequest request, CancellationToken ct)
        => Ok(await scriptService.UpdateProductionPlanAsync(id, request, ct));

    [HttpPut("{id:guid}/script/motion-prompts")]
    public async Task<ActionResult<VideoProjectDto>> SaveMotionPrompts(
        Guid id, [FromBody] SaveMotionPromptsRequest request, CancellationToken ct)
        => Ok(await scriptService.SaveMotionPromptsAsync(id, request, ct));

    [HttpPut("{id:guid}/language")]
    public async Task<ActionResult<VideoProjectDto>> SetContentLanguage(
        Guid id, [FromBody] SetContentLanguageRequest request, CancellationToken ct)
        => Ok(await projectService.SetContentLanguageAsync(id, request.Language, ct));

    [HttpPost("idea/voice-code")]
    public async Task<ActionResult<VideoProjectDto>> ConfirmVoiceCodeIdea(
        [FromBody] ConfirmManualIdeaRequest request, CancellationToken ct)
        => Ok(await voiceCode.ConfirmIdeaAsync(request, ct));

    [HttpPost("script/voice-code")]
    public async Task<ActionResult<VideoProjectDto>> SaveVoiceCodeScript(
        [FromBody] VoiceCodeScriptRequest request, CancellationToken ct)
        => Ok(await voiceCode.SaveScriptAsync(request, ct));

    [HttpGet("{id:guid}/workflow")]
    public async Task<ActionResult<ProjectWorkflowStateDto>> GetWorkflow(Guid id, CancellationToken ct)
    {
        var project = await projectService.GetByIdAsync(id, ct);
        return project?.Workflow is null ? NotFound() : Ok(project.Workflow);
    }

    [HttpPost("{id:guid}/images/generate")]
    [EnableRateLimiting("heavy")]
    public async Task<IActionResult> GenerateImages(
        Guid id, [FromBody] GenerateImagesRequest? request, CancellationToken ct)
    {
        var guardError = await ValidateWorkflowOperationAsync(id, ProjectOperationKind.ImageGeneration, ct);
        if (guardError is not null)
            return QueueEnqueueResponses.WorkflowConflict(HttpContext, id, guardError, ProjectOperationKind.ImageGeneration);

        request ??= new GenerateImagesRequest();
        var job = new ImageGenerationJob(
            id,
            request.Count,
            request.ReplaceExisting,
            ImageProviderNames.Normalize(request.Provider));

        if (imageGenerationQueue.IsActive(id))
            return QueueEnqueueResponses.AlreadyProcessing(HttpContext, id, ProjectOperationKind.ImageGeneration);

        return await imageGenerationQueue.EnqueueAsync(job, ct) switch
        {
            ImageGenerationEnqueueResult.Queued => QueueEnqueueResponses.AcceptedJob(HttpContext, id, ProjectOperationKind.ImageGeneration),
            ImageGenerationEnqueueResult.AlreadyProcessing => QueueEnqueueResponses.AlreadyProcessingWithError(HttpContext, id, ProjectOperationKind.ImageGeneration),
            _ => QueueEnqueueResponses.QueueFull(HttpContext, ProjectOperationKind.ImageGeneration)
        };
    }

    [HttpPost("{id:guid}/videos/generate")]
    [EnableRateLimiting("heavy")]
    public async Task<IActionResult> GenerateVideos(
        Guid id, [FromBody] GenerateVideosRequest? request, CancellationToken ct)
    {
        var guardError = await ValidateWorkflowOperationAsync(id, ProjectOperationKind.VideoGeneration, ct);
        if (guardError is not null)
            return QueueEnqueueResponses.WorkflowConflict(HttpContext, id, guardError, ProjectOperationKind.VideoGeneration);

        request ??= new GenerateVideosRequest();
        var job = new VideoGenerationJob(
            id,
            VideoGenerationMode.TextToVideo,
            request.Count,
            request.ReplaceExisting,
            VideoProviderNames.Normalize(request.Provider));

        if (videoGenerationQueue.IsActive(id))
            return QueueEnqueueResponses.AlreadyProcessing(HttpContext, id, ProjectOperationKind.VideoGeneration);

        return await videoGenerationQueue.EnqueueAsync(job, ct) switch
        {
            VideoGenerationEnqueueResult.Queued => QueueEnqueueResponses.AcceptedJob(HttpContext, id, ProjectOperationKind.VideoGeneration),
            VideoGenerationEnqueueResult.AlreadyProcessing => QueueEnqueueResponses.AlreadyProcessingWithError(HttpContext, id, ProjectOperationKind.VideoGeneration),
            _ => QueueEnqueueResponses.QueueFull(HttpContext, ProjectOperationKind.VideoGeneration)
        };
    }

    [HttpPost("{id:guid}/videos/animate-images")]
    [EnableRateLimiting("heavy")]
    public async Task<IActionResult> AnimateImages(
        Guid id, [FromBody] AnimateImagesRequest? request, CancellationToken ct)
    {
        var guardError = await ValidateWorkflowOperationAsync(id, ProjectOperationKind.VideoAnimation, ct);
        if (guardError is not null)
            return QueueEnqueueResponses.WorkflowConflict(HttpContext, id, guardError, ProjectOperationKind.VideoAnimation);

        request ??= new AnimateImagesRequest();
        var job = new VideoGenerationJob(
            id,
            VideoGenerationMode.AnimateImages,
            0,
            request.ReplaceClips,
            VideoProviderNames.Normalize(request.Provider));

        if (videoGenerationQueue.IsActive(id))
            return QueueEnqueueResponses.AlreadyProcessing(HttpContext, id, ProjectOperationKind.VideoAnimation);

        return await videoGenerationQueue.EnqueueAsync(job, ct) switch
        {
            VideoGenerationEnqueueResult.Queued => QueueEnqueueResponses.AcceptedJob(HttpContext, id, ProjectOperationKind.VideoAnimation),
            VideoGenerationEnqueueResult.AlreadyProcessing => QueueEnqueueResponses.AlreadyProcessingWithError(HttpContext, id, ProjectOperationKind.VideoAnimation),
            _ => QueueEnqueueResponses.QueueFull(HttpContext, ProjectOperationKind.VideoAnimation)
        };
    }

    [HttpPost("{id:guid}/images/{imageId:int}/regenerate")]
    [EnableRateLimiting("heavy")]
    public async Task<ActionResult<VideoProjectDto>> RegenerateImage(
        Guid id, int imageId, [FromBody] RegenerateImageRequest? request, CancellationToken ct)
        => Ok(await imageGenerator.RegenerateImageAsync(
            id, imageId,
            ImageProviderNames.Normalize(request?.Provider),
            ct));

    [HttpDelete("{id:guid}/images/{imageId:int}")]
    public async Task<ActionResult<VideoProjectDto>> DeleteImage(
        Guid id, int imageId, CancellationToken ct)
        => Ok(await imageGenerator.DeleteImageAsync(id, imageId, ct));

    [HttpPost("{id:guid}/images/{imageId:int}/replace")]
    [RequestSizeLimit(50_000_000)]
    public async Task<ActionResult<VideoProjectDto>> ReplaceImage(
        Guid id, int imageId, [FromForm] IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("لم يتم رفع أي ملف.");

        await using var stream = file.OpenReadStream();
        return Ok(await imageGenerator.ReplaceImageAsync(id, imageId, stream, file.FileName, ct));
    }

    [HttpPut("{id:guid}/images/{imageId:int}/animation")]
    public async Task<ActionResult<VideoProjectDto>> SetImageAnimation(
        Guid id, int imageId, [FromBody] SetImageAnimationRequest request, CancellationToken ct)
        => Ok(await imageGenerator.SetImageAnimationAsync(
            id, imageId, request.AnimationMode, request.MotionSpeed, request.MaxZoom, ct));

    [HttpPut("{id:guid}/images/animations")]
    public async Task<ActionResult<VideoProjectDto>> SetBulkImageAnimations(
        Guid id, [FromBody] BulkImageAnimationRequest request, CancellationToken ct)
    {
        var updates = request.Animations
            .Select(a => (a.ImageId, a.AnimationMode, a.MotionSpeed, a.MaxZoom))
            .ToList();
        return Ok(await imageGenerator.SetBulkImageAnimationsAsync(id, updates, ct));
    }

    [HttpPost("{id:guid}/images/animations/auto")]
    public async Task<ActionResult<VideoProjectDto>> ApplyAutoMotion(Guid id, CancellationToken ct)
        => Ok(await imageGenerator.ApplyAutoMotionAssignmentsAsync(id, ct));

    [HttpPost("{id:guid}/audio/generate")]
    [EnableRateLimiting("heavy")]
    public async Task<ActionResult<VideoProjectDto>> GenerateAudio(
        Guid id, [FromBody] GenerateAudioRequest? request, CancellationToken ct)
        => Ok(await ttsService.GenerateProjectAudioAsync(id, request, ct));

    [HttpPost("{id:guid}/segments/{sortOrder:int}/media/{partIndex:int}")]
    [RequestSizeLimit(200_000_000)]
    public async Task<ActionResult<VideoProjectDto>> UploadVoiceCodeSegmentMedia(
        Guid id, int sortOrder, int partIndex, [FromForm] IFormFile file, CancellationToken ct)
    {
        var guardError = await ValidateWorkflowOperationAsync(id, ProjectOperationKind.VoiceCodeMediaUpload, ct);
        if (guardError is not null)
            return Conflict(new { error = guardError, projectId = id });

        if (file is null || file.Length == 0)
            return BadRequest("لم يتم رفع ملف للمقطع.");

        await using var stream = file.OpenReadStream();
        return Ok(await voiceCode.UploadSegmentMediaAsync(id, sortOrder, partIndex, stream, file.FileName, ct));
    }

    [HttpPost("{id:guid}/segments/{sortOrder:int}/media")]
    [RequestSizeLimit(200_000_000)]
    public async Task<ActionResult<VideoProjectDto>> UploadVoiceCodeSegmentMediaLegacy(
        Guid id, int sortOrder, [FromForm] IFormFile file, CancellationToken ct)
    {
        var guardError = await ValidateWorkflowOperationAsync(id, ProjectOperationKind.VoiceCodeMediaUpload, ct);
        if (guardError is not null)
            return Conflict(new { error = guardError, projectId = id });

        if (file is null || file.Length == 0)
            return BadRequest("لم يتم رفع ملف للمقطع.");

        await using var stream = file.OpenReadStream();
        return Ok(await voiceCode.UploadSegmentMediaAsync(id, sortOrder, 0, stream, file.FileName, ct));
    }

    [HttpGet("{id:guid}/video/file")]
    public async Task<IActionResult> GetFinalVideoFile(Guid id, CancellationToken ct)
    {
        var project = await projectService.GetByIdAsync(id, ct);
        if (project?.VideoFilePath is null) return NotFound();

        var absolute = paths.ToAbsolutePath(project.VideoFilePath);
        if (!System.IO.File.Exists(absolute)) return NotFound();

        return PhysicalFile(absolute, ResolveVideoContentType(absolute), enableRangeProcessing: true);
    }

    [HttpGet("{id:guid}/images/{imageId:int}/file")]
    public async Task<IActionResult> GetImageFile(Guid id, int imageId, CancellationToken ct)
    {
        var project = await projectService.GetByIdAsync(id, ct);
        if (project is null) return NotFound();

        var image = project.Images.FirstOrDefault(i => i.Id == imageId);
        if (image?.LocalFilePath is null) return NotFound();

        var absolute = await ResolveImageAbsolutePathAsync(id, image.LocalFilePath, image.SortOrder, ct);
        if (absolute is null || !System.IO.File.Exists(absolute)) return NotFound();

        if (image.IsVideoClip)
            return PhysicalFile(absolute, ResolveVideoContentType(absolute), enableRangeProcessing: true);

        var contentType = Path.GetExtension(absolute).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };

        return PhysicalFile(absolute, contentType);
    }

    [HttpPost("{id:guid}/produce")]
    [EnableRateLimiting("heavy")]
    public async Task<IActionResult> StartProduction(Guid id, CancellationToken ct)
    {
        var project = await projectService.GetByIdAsync(id, ct);
        if (project is null) return NotFound();

        if (project.Status == VideoProjectStatus.Processing || productionQueue.IsActive(id))
            return QueueEnqueueResponses.AlreadyProcessing(HttpContext, id, ProjectOperationKind.Production);

        if (project.Script is null)
        {
            var noScript = OperationHints.WorkflowBlocked("لا يوجد سكريبت — ولّد السكريبت أولاً.", ProjectOperationKind.ScriptGeneration);
            return new ConflictObjectResult(ApiErrorWriter.ToBody(noScript, HttpContext));
        }

        var produceGuard = ProjectWorkflowGuard.ValidateOperation(project.Workflow, ProjectOperationKind.Production);
        if (produceGuard is not null)
            return QueueEnqueueResponses.WorkflowConflict(HttpContext, id, produceGuard, ProjectOperationKind.Production);

        var result = await productionQueue.EnqueueAsync(id, ct);
        return result switch
        {
            ProductionEnqueueResult.Queued => QueueEnqueueResponses.AcceptedJob(HttpContext, id, ProjectOperationKind.Production),
            ProductionEnqueueResult.AlreadyProcessing => QueueEnqueueResponses.AlreadyProcessing(HttpContext, id, ProjectOperationKind.Production),
            ProductionEnqueueResult.QueueFull => QueueEnqueueResponses.QueueFull(HttpContext, ProjectOperationKind.Production),
            _ => new ConflictObjectResult(ApiErrorWriter.ToBody(
                OperationHints.WorkflowBlocked("تعذر إضافة المشروع لقائمة الإنتاج.", ProjectOperationKind.Production),
                HttpContext))
        };
    }

    private static string ResolveVideoContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            ".mkv" => "video/x-matroska",
            _ => "video/mp4"
        };

    private async Task<string?> ResolveImageAbsolutePathAsync(
        Guid projectId, string storedPath, int sortOrder, CancellationToken ct)
    {
        try
        {
            var configuredOutput = await settings.GetOrDefaultAsync(SettingKeys.OutputDirectory, "output", ct);
            return paths.ResolveSceneMediaPath(configuredOutput, projectId, sortOrder, storedPath);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> ValidateWorkflowOperationAsync(
        Guid projectId, ProjectOperationKind kind, CancellationToken ct)
    {
        var project = await projectService.GetByIdAsync(projectId, ct);
        if (project is null)
            return OperationHints.ProjectNotFound().Error;
        return ProjectWorkflowGuard.ValidateOperation(project.Workflow, kind);
    }
}

