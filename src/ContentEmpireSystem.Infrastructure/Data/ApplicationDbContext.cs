using ContentEmpireSystem.Core.Entities;

using ContentEmpireSystem.Core.Interfaces;

using ContentEmpireSystem.Infrastructure.Auth;

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;



namespace ContentEmpireSystem.Infrastructure.Data;



public class ApplicationDbContext(

    DbContextOptions<ApplicationDbContext> options,

    ICurrentUserAccessor currentUser) : IdentityDbContext<ApplicationUser>(options)

{

    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    public DbSet<ContentCategory> ContentCategories => Set<ContentCategory>();

    public DbSet<VideoProject> VideoProjects => Set<VideoProject>();

    public DbSet<GeneratedImage> GeneratedImages => Set<GeneratedImage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)

    {

        base.OnModelCreating(modelBuilder);



        modelBuilder.Entity<SystemSetting>(e =>

        {

            e.HasIndex(x => x.Key).IsUnique();

            e.Property(x => x.Key).HasMaxLength(128);

            e.Property(x => x.Category).HasMaxLength(64);

        });



        modelBuilder.Entity<ContentCategory>(e =>

        {

            e.HasIndex(x => x.CategoryType).IsUnique();

            e.Property(x => x.NameAr).HasMaxLength(256);

            e.Property(x => x.NameEn).HasMaxLength(256);

        });



        modelBuilder.Entity<VideoProject>(e =>

        {

            e.HasIndex(x => x.Status);

            e.HasIndex(x => x.CreatedAt);

            e.HasIndex(x => x.OwnerUserId);

            e.HasIndex(x => new { x.OwnerUserId, x.UpdatedAt });

            e.Property(x => x.OwnerUserId).HasMaxLength(450);



            e.HasQueryFilter(p =>

                currentUser.IsAdmin

                || (currentUser.UserId != null && p.OwnerUserId == currentUser.UserId));

        });



        modelBuilder.Entity<GeneratedImage>(e =>

        {

            e.HasOne(x => x.VideoProject)

                .WithMany(x => x.GeneratedImages)

                .HasForeignKey(x => x.VideoProjectId)

                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}


