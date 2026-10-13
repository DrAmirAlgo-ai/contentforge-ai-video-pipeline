namespace ContentEmpireSystem.Core;

public static class GpuStatusEstimator
{
    public static int EstimateWaitSeconds(int waitQueueCount, bool isBusy, int busyElapsedSec, string? workloadLabel)
    {
        var perWaiter = AverageJobSeconds(workloadLabel);
        var remaining = isBusy ? Math.Max(15, perWaiter - busyElapsedSec) : 0;
        return waitQueueCount * perWaiter + remaining;
    }

    public static int AverageJobSeconds(string? workloadLabel)
    {
        if (string.IsNullOrWhiteSpace(workloadLabel))
            return 120;

        var label = workloadLabel.ToLowerInvariant();
        if (label.Contains("wan"))
            return 240;
        if (label.Contains("flux") || label.Contains("comfy"))
            return 90;
        if (label.Contains("qwen") || label.Contains("script") || label.Contains("lm"))
            return 75;

        return 120;
    }
}
