using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fatora.DAL.Data.Configuration;

public class SupportContactConfiguration : IEntityTypeConfiguration<SupportContact>
{
    public void Configure(EntityTypeBuilder<SupportContact> builder)
    {
        builder.Property(x => x.Type).HasConversion<string>();
        builder.Property(x => x.Value).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Label).HasMaxLength(100);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => x.DisplayOrder);
    }
}
