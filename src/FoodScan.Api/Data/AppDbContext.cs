using FoodScan.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FoodScan.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<Component> Components => Set<Component>();
    public DbSet<TrackedComponent> TrackedComponents => Set<TrackedComponent>();
    public DbSet<Analysis> Analyses => Set<Analysis>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Persist UTC instants consistently across PostgreSQL and SQLite test databases.
        b.Entity<User>().Property(x => x.CreatedAt).HasConversion(x => x.UtcDateTime,
            x => new DateTimeOffset(DateTime.SpecifyKind(x, DateTimeKind.Utc)));
        b.Entity<Analysis>().Property(x => x.CreatedAt).HasConversion(x => x.UtcDateTime,
            x => new DateTimeOffset(DateTime.SpecifyKind(x, DateTimeKind.Utc)));
        b.Entity<RefreshSession>().Property(x => x.ExpiresAt).HasConversion(x => x.UtcDateTime,
            x => new DateTimeOffset(DateTime.SpecifyKind(x, DateTimeKind.Utc)));
        b.Entity<User>().HasIndex(x => x.NormalizedEmail).IsUnique();
        b.Entity<RefreshSession>().HasIndex(x => x.TokenHash).IsUnique();
        b.Entity<RefreshSession>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<RefreshSession>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<TrackedComponent>().HasKey(x => new { x.UserId, x.ComponentId });
        b.Entity<TrackedComponent>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<TrackedComponent>().HasOne<Component>().WithMany().HasForeignKey(x => x.ComponentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Analysis>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Analysis>().HasIndex(x => new { x.UserId, x.CreatedAt });
        // JSON text is deliberately portable for SQLite integration tests.
        b.Entity<Component>().HasData(Catalog.All);
    }
}
