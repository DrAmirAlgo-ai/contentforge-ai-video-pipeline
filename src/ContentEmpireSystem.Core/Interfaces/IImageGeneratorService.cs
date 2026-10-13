using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Enums;

namespace ContentEmpireSystem.Core.Interfaces;

public interface IImageGeneratorService
{
    Task GenerateImagesForProjectAsync(Guid projectId, CancellationToken ct = default);
    Task<VideoProjectDto> GenerateImagesAsync(
        Guid projectId, int count, bool replaceExisting, string? provider = null, CancellationToken ct = default);
    Task<VideoProjectDto> RegenerateImageAsync(
        Guid projectId, int imageId, string? provider = null, CancellationToken ct = default);
    Task<VideoProjectDto> DeleteImageAsync(Guid projectId, int imageId, CancellationToken ct = default);
    Task<VideoProjectDto> ReplaceImageAsync(Guid projectId, int imageId, Stream file, string fileName, CancellationToken ct = default);
    Task<VideoProjectDto> SetImageAnimationAsync(
        Guid projectId, int imageId, string animationMode, double motionSpeed, double maxZoom, CancellationToken ct = default);
    Task<VideoProjectDto> SetBulkImageAnimationsAsync(
        Guid projectId,
        IReadOnlyList<(int ImageId, string AnimationMode, double MotionSpeed, double MaxZoom)> updates,
        CancellationToken ct = default);
    Task<VideoProjectDto> ApplyAutoMotionAssignmentsAsync(Guid projectId, CancellationToken ct = default);
}
