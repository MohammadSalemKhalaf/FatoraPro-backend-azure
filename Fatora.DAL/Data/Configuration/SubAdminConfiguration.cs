using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fatora.DAL.Data.Configuration;

public class SubAdminConfiguration : IEntityTypeConfiguration<SubAdmin>
{
    public void Configure(EntityTypeBuilder<SubAdmin> builder)
    {
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.QrToken).IsRequired().HasMaxLength(100);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => x.QrToken).IsUnique();
    }
}
