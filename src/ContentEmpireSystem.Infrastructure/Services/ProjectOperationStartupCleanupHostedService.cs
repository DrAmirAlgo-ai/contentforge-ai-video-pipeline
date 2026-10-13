using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ContentEmpireSystem.Infrastructure.Services;

/// <summary>Clears stale per-project locks left after API crash/restart.</summary>
public sealed class ProjectOperationStartupCleanupHostedService(
    IProjectOperationCoordinator coordinator,
    ILogger<ProjectOperationStartupCleanupHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        coordinator.ClearAll();
        logger.LogInformation("Cleared stale project operation locks after startup.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
