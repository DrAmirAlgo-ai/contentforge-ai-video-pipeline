using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Enums;

namespace ContentEmpireSystem.Core.Interfaces;

public interface IFfmpegVideoService
{
    Task<string> RenderVideoAsync(Guid projectId, CancellationToken ct = default);
}
