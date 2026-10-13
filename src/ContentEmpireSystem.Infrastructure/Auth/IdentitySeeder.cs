using ContentEmpireSystem.Core.Entities;
using ContentEmpireSystem.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentEmpireSystem.Infrastructure.Auth;

public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("IdentitySeeder");
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var db = services.GetRequiredService<ApplicationDbContext>();
        var authOptions = services.GetRequiredService<IOptions<AuthOptions>>().Value;

        foreach (var role in new[] { AuthRoles.Admin, AuthRoles.User })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        ApplicationUser? adminUser = null;
        if (authOptions.BootstrapAdmin.Enabled && !await userManager.Users.AnyAsync(ct))
        {
            var email = authOptions.BootstrapAdmin.Email.Trim().ToLowerInvariant();
            adminUser = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = authOptions.BootstrapAdmin.DisplayName
            };

            var created = await userManager.CreateAsync(adminUser, authOptions.BootstrapAdmin.Password);
            if (created.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, AuthRoles.Admin);
                await userManager.AddToRoleAsync(adminUser, AuthRoles.User);
                logger.LogInformation("Bootstrap admin created: {Email}", email);
            }
            else
            {
                logger.LogWarning("Bootstrap admin failed: {Errors}",
                    string.Join("; ", created.Errors.Select(e => e.Description)));
                adminUser = null;
            }
        }

        adminUser ??= await userManager.Users
            .FirstOrDefaultAsync(u => u.Email == authOptions.BootstrapAdmin.Email.Trim().ToLowerInvariant(), ct);

        if (adminUser is not null)
        {
            var orphaned = await db.VideoProjects
                .Where(p => p.OwnerUserId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.OwnerUserId, adminUser.Id), ct);
            if (orphaned > 0)
                logger.LogInformation("Assigned {Count} legacy projects to admin", orphaned);
        }
    }
}
