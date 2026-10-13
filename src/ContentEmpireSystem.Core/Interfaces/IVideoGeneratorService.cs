using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Enums;

namespace ContentEmpireSystem.Core.Interfaces;

public interface IVideoGeneratorService
{
    /// <summary>Path 1: animate existing FLUX images with Wan TI2V using script videoSuggestions.</summary>
    Task<VideoProjectDto> AnimateImagesToVideoAsync(
        Guid projectId, bool replaceClips = false, string? provider = null, CancellationToken ct = default);

    /// <summary>Path 2: generate video clips directly from script videoSuggestions (Wan T2V).</summary>
    Task<VideoProjectDto> GenerateVideosFromPromptsAsync(
        Guid projectId, int count, bool replaceExisting, string? provider = null, CancellationToken ct = default);
}
