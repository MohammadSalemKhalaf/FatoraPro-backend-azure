using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fatora.DAL.Data.Configuration;

public class SubAdminRefreshTokenConfiguration : IEntityTypeConfiguration<SubAdminRefreshToken>
{
    public void Configure(EntityTypeBuilder<SubAdminRefreshToken> builder)
    {
        builder.Property(x => x.Token).IsRequired().HasMaxLength(200);

        builder.HasIndex(x => x.Token).IsUnique();

        builder.HasOne(x => x.SubAdmin)
            .WithMany()
            .HasForeignKey(x => x.SubAdminId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
