using ContentEmpireSystem.Core.DTOs;

namespace ContentEmpireSystem.Core.Interfaces;

public interface ISystemActivityService
{
    IReadOnlyList<ActivityEntryDto> GetSnapshot();
    event Action? Changed;
}
