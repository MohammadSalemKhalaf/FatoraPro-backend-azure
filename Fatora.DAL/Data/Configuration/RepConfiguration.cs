using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fatora.DAL.Data.Configuration;

public class RepConfiguration : IEntityTypeConfiguration<Rep>
{
    public void Configure(EntityTypeBuilder<Rep> builder)
    {
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.QrToken).IsRequired().HasMaxLength(100);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.ProductAccessMode).HasConversion<string>();
        builder.Property(x => x.CustomerAccessMode).HasConversion<string>();

        builder.HasIndex(x => x.QrToken).IsUnique();
        builder.HasIndex(x => x.OwnerUserId);

        builder.HasOne(x => x.OwnerUser).WithMany().HasForeignKey(x => x.OwnerUserId);
    }
}
