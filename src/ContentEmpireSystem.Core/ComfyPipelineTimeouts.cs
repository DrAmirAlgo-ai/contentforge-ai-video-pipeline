namespace ContentEmpireSystem.Core;

/// <summary>Shared ComfyUI job timeout defaults (minutes) — backend, dashboard, and E2E scripts.</summary>
public static class ComfyPipelineTimeouts
{
    public const int FluxWorkflowMinutes = 30;
    public const int WanTi2vWorkflowMinutes = 90;
    public const int WanT2vWorkflowMinutes = 60;
    public const int SettingDefaultMinutes = 90;
    public const int FrontendPollBufferMinutes = 5;

    public static int FrontendPollMs(int backendMinutes) =>
        (backendMinutes + FrontendPollBufferMinutes) * 60 * 1000;
}
