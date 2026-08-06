using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fatora.DAL.Data.Configuration;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasIndex(x => new { x.UserId, x.UpdatedAt });

        // Filtered so multiple products can still have a NULL barcode -
        // uniqueness only matters once a barcode is actually set, since the
        // scanner needs it to resolve to exactly one product.
        builder.HasIndex(x => new { x.UserId, x.Barcode })
            .IsUnique()
            .HasFilter("\"Barcode\" IS NOT NULL");

        builder.HasIndex(x => x.CreatedByRepId);

        // See OrderConfiguration.CreatedByRep for why this is Restrict.
        builder.HasOne(x => x.CreatedByRep)
            .WithMany()
            .HasForeignKey(x => x.CreatedByRepId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
