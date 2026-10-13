using System.Diagnostics;
using ContentEmpireSystem.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ContentEmpireSystem.Infrastructure.Data;

public sealed class DatabaseBootstrapHostedService(
    IServiceScopeFactory scopeFactory,
    IDatabaseBootstrapState bootstrapState,
    DatabaseOptions databaseOptions,
    ILogger<DatabaseBootstrapHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        bootstrapState.MarkStarting(databaseOptions.Provider);
        _ = BootstrapAsync(cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task BootstrapAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (databaseOptions.BootstrapTimeoutSeconds > 0)
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(databaseOptions.BootstrapTimeoutSeconds));
        var ct = timeoutCts.Token;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await db.Database.MigrateAsync(ct);
            await DatabaseSeeder.ApplyLegacySchemaPatchesAsync(db, databaseOptions, ct);

            await DatabaseSeeder.SeedDataAsync(db, ct);
            await IdentitySeeder.SeedAsync(scope.ServiceProvider, ct);

            sw.Stop();
            bootstrapState.MarkReady(sw.Elapsed);
            logger.LogInformation(
                "Database ready ({Provider}) in {ElapsedMs} ms",
                databaseOptions.Provider,
                sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            bootstrapState.MarkFailed(new TimeoutException(
                $"Database bootstrap exceeded {databaseOptions.BootstrapTimeoutSeconds}s."));
            logger.LogCritical(
                "Database bootstrap timed out after {ElapsedMs} ms ({Provider})",
                sw.ElapsedMilliseconds,
                databaseOptions.Provider);
            return;
        }
        catch (Exception ex)
        {
            sw.Stop();
            bootstrapState.MarkFailed(ex);
            logger.LogCritical(
                ex,
                "Database bootstrap failed after {ElapsedMs} ms ({Provider})",
                sw.ElapsedMilliseconds,
                databaseOptions.Provider);
        }
    }
}
