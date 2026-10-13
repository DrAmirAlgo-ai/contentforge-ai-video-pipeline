using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Enums;

namespace ContentEmpireSystem.Core.Interfaces;

public interface IVideoProjectService
{
    Task<List<VideoProjectDto>> GetQueueAsync(VideoProjectStatus? status = null, CancellationToken ct = default);
    Task<VideoProjectDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<VideoProjectDto> SetContentLanguageAsync(Guid projectId, string language, CancellationToken ct = default);
}
