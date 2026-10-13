using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Entities;

namespace ContentEmpireSystem.Core.Interfaces;

public interface IProjectWorkflowBuilder
{
    Task<ProjectWorkflowStateDto> BuildAsync(VideoProject project, CancellationToken ct = default);

    ProjectWorkflowStateDto Build(VideoProject project, bool videoGenerationSupported);
}
