namespace ContentEmpireSystem.Core.DTOs;

public sealed class GpuStatusDto
{
    public bool IsBusy { get; set; }
    public int WaitQueueCount { get; set; }
    public int BusyElapsedSec { get; set; }
    public int EstimatedWaitSec { get; set; }
    public string? CurrentWorkload { get; set; }
}
