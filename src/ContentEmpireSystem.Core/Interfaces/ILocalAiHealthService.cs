using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Enums;

namespace ContentEmpireSystem.Core.Interfaces;

public interface ILocalAiHealthService
{
    Task<LocalAiHealthDto> CheckAsync(CancellationToken ct = default);
}
