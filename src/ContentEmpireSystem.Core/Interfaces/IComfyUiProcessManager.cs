namespace ContentEmpireSystem.Core.Interfaces;

/// <summary>Stops and starts ComfyUI Desktop to free VRAM between FLUX and Wan on low-VRAM GPUs.</summary>
public interface IComfyUiProcessManager
{
    /// <summary>Restart ComfyUI once after a complete FLUX image batch (marks grace window for Wan).</summary>
    Task RestartAfterFluxBatchAsync(CancellationToken ct = default);

    /// <summary>Skip if recently restarted; otherwise restart when auto-restart is enabled.</summary>
    Task EnsureReadyForWanAsync(CancellationToken ct = default);

    bool WasRecentlyRestartedAfterFlux(TimeSpan graceWindow);
}
