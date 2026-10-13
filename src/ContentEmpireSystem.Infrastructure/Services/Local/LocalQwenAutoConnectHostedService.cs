using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ContentEmpireSystem.Infrastructure.Services.Local;

/// <summary>On startup, auto-connect Qwen (LM Studio or Ollama) in the background.</summary>
public sealed class LocalQwenAutoConnectHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<LocalQwenAutoConnectHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= 6 && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var qwenConnector = scope.ServiceProvider.GetRequiredService<ILocalQwenConnector>();
                var conn = await qwenConnector.ResolveLiveAsync(stoppingToken);
                logger.LogInformation(
                    "Qwen auto-connect OK (attempt {Attempt}): {Source} → {Model}",
                    attempt, conn.SourceLabel, conn.Model);
                return;
            }
            catch (Exception ex) when (attempt < 6)
            {
                logger.LogWarning(ex, "Qwen auto-connect attempt {Attempt} failed — retry in 15s", attempt);
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Qwen auto-connect failed after {Attempt} attempts", attempt);
            }
        }
    }
}
