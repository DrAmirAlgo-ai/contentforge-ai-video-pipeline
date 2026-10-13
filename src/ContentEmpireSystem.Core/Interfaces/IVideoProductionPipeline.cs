using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Enums;

namespace ContentEmpireSystem.Core.Interfaces;

public interface IVideoProductionPipeline
{
    Task StartProductionAsync(Guid projectId, CancellationToken ct = default);
}
