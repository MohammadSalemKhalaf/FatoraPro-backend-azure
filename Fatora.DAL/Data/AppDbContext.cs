using Fatora.DAL.Data.Configuration;
using Fatora.DAL.Entites;
using Fatora.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fatora.DAL.Data;


public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{

    public DbSet<User> Users { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<Product> Products { get; set; }
    public DbSet<Customer> Customers { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<PasswordResetOtp> PasswordResetOtps { get; set; }

    public DbSet<Order> Orders { get; set; }
    public DbSet<Receipt> Receipts { get; set; }
    public DbSet<Rep> Reps { get; set; }
    public DbSet<RepRefreshToken> RepRefreshTokens { get; set; }
    public DbSet<RepProductAccess> RepProductAccesses { get; set; }
    public DbSet<RepCustomerAccess> RepCustomerAccesses { get; set; }
    public DbSet<PendingRepSync> PendingRepSyncs { get; set; }
    public DbSet<AnnualInventoryArchive> AnnualInventoryArchives { get; set; }
    public DbSet<SubAdmin> SubAdmins { get; set; }
    public DbSet<SubAdminRefreshToken> SubAdminRefreshTokens { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public override int SaveChanges()
    {
        StampSyncTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampSyncTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    // The sync push endpoint must preserve the timestamp the edit actually happened on the device
    // (possibly hours ago, while offline), not the moment the request reached the server - otherwise
    // last-write-wins conflict resolution would be meaningless. It sets CreatedAt/UpdatedAt itself and
    // flips this off for the duration of that save.
    public bool SuppressAutoTimestamps { get; set; }

    private void StampSyncTimestamps()
    {
        if (SuppressAutoTimestamps)
        {
            return;
        }

        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<ISyncableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}