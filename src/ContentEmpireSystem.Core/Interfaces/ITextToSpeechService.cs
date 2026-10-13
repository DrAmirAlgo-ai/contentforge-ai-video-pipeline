using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Enums;

namespace ContentEmpireSystem.Core.Interfaces;

public interface ITextToSpeechService
{
    Task<(string AudioPath, string TimestampsJson, string ProviderUsed)> GenerateSpeechAsync(
        Guid projectId, string text, CancellationToken ct = default);
    Task<VideoProjectDto> GenerateProjectAudioAsync(Guid projectId, GenerateAudioRequest? options, CancellationToken ct = default);
    Task<VideoProjectDto> UploadProjectAudioAsync(Guid projectId, Stream audioStream, string fileName, CancellationToken ct = default);
    Task<(string AbsolutePath, string ContentType)?> TryResolveProjectAudioFileAsync(Guid projectId, CancellationToken ct = default);
}
