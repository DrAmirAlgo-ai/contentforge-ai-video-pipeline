namespace ContentEmpireSystem.Infrastructure.Services.Local;

/// <summary>Tracks the last post-FLUX ComfyUI restart to avoid duplicate restarts in auto-pipeline.</summary>
public sealed class ComfyUiRestartState
{
    private long _lastFluxBatchRestartTicks;

    public void MarkFluxBatchRestart() =>
        Interlocked.Exchange(ref _lastFluxBatchRestartTicks, DateTime.UtcNow.Ticks);

    public bool WasRecentlyRestartedAfterFlux(TimeSpan graceWindow)
    {
        var ticks = Interlocked.Read(ref _lastFluxBatchRestartTicks);
        if (ticks <= 0)
            return false;

        var elapsed = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
        return elapsed <= graceWindow;
    }
}
