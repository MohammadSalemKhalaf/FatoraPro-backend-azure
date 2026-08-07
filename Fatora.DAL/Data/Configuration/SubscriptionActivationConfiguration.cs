using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fatora.DAL.Data.Configuration;

public class SubscriptionActivationConfiguration : IEntityTypeConfiguration<SubscriptionActivation>
{
    public void Configure(EntityTypeBuilder<SubscriptionActivation> builder)
    {
        builder.Property(x => x.SubscriptionType).HasConversion<string>();

        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => new { x.PerformedBySubAdminId, x.ActivatedAt });

        // Cascade (the default for a required FK, left implicit here to
        // match Order/Customer/Product's own User relationship): a
        // subscriber can hard-delete their own account
        // (UserService.DeleteAccountAsync), and their activation history is
        // meaningless once that happens - Restrict here would silently
        // break that already-working self-service flow the moment any
        // subscriber had ever been activated.
        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId);

        // Restrict, not cascade: a SubAdmin is only ever soft-deactivated/
        // hidden (see SubAdmin.cs), never actually removed, so this never
        // fires in practice - but it documents that the audit trail must
        // keep resolving even if that ever changes. Same reasoning as
        // Order.CreatedByRepId / User.ManagedBySubAdminId.
        builder.HasOne(x => x.PerformedBySubAdmin)
            .WithMany()
            .HasForeignKey(x => x.PerformedBySubAdminId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
