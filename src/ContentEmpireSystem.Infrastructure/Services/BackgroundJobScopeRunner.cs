using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ContentEmpireSystem.Infrastructure.Services;

internal static class BackgroundJobScopeRunner
{
    public static async Task RunAsync(
        IServiceScopeFactory scopeFactory,
        Guid? projectId,
        Func<IServiceProvider, CancellationToken, Task> action,
        CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        try
        {
            if (projectId.HasValue)
                await ImpersonateProjectOwnerAsync(scope.ServiceProvider, projectId.Value, ct);

            await action(scope.ServiceProvider, ct);
        }
        finally
        {
            scope.ServiceProvider.GetService<IBackgroundJobUserContext>()?.Clear();
        }
    }

    private static async Task ImpersonateProjectOwnerAsync(
        IServiceProvider services, Guid projectId, CancellationToken ct)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var owner = await db.VideoProjects
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => p.OwnerUserId)
            .FirstOrDefaultAsync(ct);

        var bg = services.GetRequiredService<IBackgroundJobUserContext>();
        if (!string.IsNullOrWhiteSpace(owner))
            bg.Set(owner);
        else
            bg.Set(null, isAdmin: true);
    }
}
