using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fatora.DAL.Data.Configuration;

public class PurchaseRequestConfiguration : IEntityTypeConfiguration<PurchaseRequest>
{
    public void Configure(EntityTypeBuilder<PurchaseRequest> builder)
    {
        builder.Navigation(x => x.Items).AutoInclude();
        builder.HasIndex(x => new { x.UserId, x.UpdatedAt });
        builder.HasIndex(x => x.CreatedByRepId);
        builder.HasOne(x => x.CreatedByRep).WithMany().HasForeignKey(x => x.CreatedByRepId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Items).WithOne(x => x.PurchaseRequest)
            .HasForeignKey(x => x.PurchaseRequestId).OnDelete(DeleteBehavior.Cascade);
    }
}
