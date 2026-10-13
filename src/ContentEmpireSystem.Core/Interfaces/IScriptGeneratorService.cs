using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Enums;

namespace ContentEmpireSystem.Core.Interfaces;

public interface IScriptGeneratorService
{
    Task<VideoProjectDto> GenerateScriptAsync(
        Guid projectId,
        string? language = null,
        bool useScriptPromptOnly = false,
        int targetWordCount = ScriptGenerationHelper.DefaultWordCount,
        int imageSegmentCount = 0,
        string? productionWorkflowPath = null,
        CancellationToken ct = default);

    Task<VideoProjectDto> UpdateProductionPlanAsync(
        Guid projectId, UpdateProductionPlanRequest request, CancellationToken ct = default);

    Task<VideoProjectDto> SaveMotionPromptsAsync(
        Guid projectId, SaveMotionPromptsRequest request, CancellationToken ct = default);
}
