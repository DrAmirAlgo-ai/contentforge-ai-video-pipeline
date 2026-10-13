using ContentEmpireSystem.Core.DTOs;

namespace ContentEmpireSystem.Core.Interfaces;

public interface IIdeaGeneratorService
{
    Task<VideoProjectDto> GenerateIdeaAsync(
        int? categoryId,
        Guid? existingProjectId,
        string? language = null,
        bool useScriptPromptOnly = false,
        CancellationToken ct = default);

    Task<VideoProjectDto> SkipIdeaAsync(Guid projectId, string? language = null, CancellationToken ct = default);
    Task<VideoProjectDto> ConfirmIdeaAsync(Guid projectId, CancellationToken ct = default);
}
