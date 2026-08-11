using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fatora.DAL.Data.Configuration;

public class SubAdminAccountActionConfiguration : IEntityTypeConfiguration<SubAdminAccountAction>
{
    public void Configure(EntityTypeBuilder<SubAdminAccountAction> builder)
    {
        builder.Property(x => x.ActionType).HasConversion<string>();

        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => new { x.PerformedBySubAdminId, x.CreatedAt });

        // Same reasoning as SubscriptionActivationConfiguration: a
        // subscriber can hard-delete their own account, and this history
        // is meaningless once that happens.
        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId);

        // Restrict, not cascade - same reasoning as
        // SubscriptionActivationConfiguration.PerformedBySubAdmin.
        builder.HasOne(x => x.PerformedBySubAdmin)
            .WithMany()
            .HasForeignKey(x => x.PerformedBySubAdminId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
