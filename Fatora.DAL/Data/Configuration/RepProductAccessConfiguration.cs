using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fatora.DAL.Data.Configuration;

public class RepProductAccessConfiguration : IEntityTypeConfiguration<RepProductAccess>
{
    public void Configure(EntityTypeBuilder<RepProductAccess> builder)
    {
        builder.HasKey(x => new { x.RepId, x.ProductId });

        // Cascade on both sides: an access-grant row only ever means
        // anything alongside the Rep and Product it names, so it's
        // meaningless (and should disappear) the moment either is gone -
        // unlike CreatedByRepId on Order/Customer, there's no historical
        // record here worth preserving.
        builder.HasOne(x => x.Rep).WithMany().HasForeignKey(x => x.RepId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}
