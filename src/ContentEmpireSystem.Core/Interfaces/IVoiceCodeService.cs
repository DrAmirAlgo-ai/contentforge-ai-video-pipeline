using ContentEmpireSystem.Core.DTOs;

namespace ContentEmpireSystem.Core.Interfaces;

public interface IVoiceCodeService
{
    Task<VideoProjectDto> ConfirmIdeaAsync(ConfirmManualIdeaRequest request, CancellationToken ct = default);
    Task<VideoProjectDto> SaveScriptAsync(VoiceCodeScriptRequest request, CancellationToken ct = default);
    Task<VideoProjectDto> UploadSegmentMediaAsync(
        Guid projectId, int sortOrder, int partIndex, Stream file, string fileName, CancellationToken ct = default);
}
