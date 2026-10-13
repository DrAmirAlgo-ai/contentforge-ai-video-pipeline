using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Enums;

namespace ContentEmpireSystem.Core.Interfaces;

public interface ILocalGpuOrchestrator
{
    /// <summary>Serializes local GPU workloads (Qwen â†’ Flux â†’ Wan) so only one model uses VRAM at a time.</summary>
    Task<IAsyncDisposable> AcquireGpuAsync(string? workloadLabel = null, CancellationToken ct = default);

    GpuStatusDto GetStatus();
}
