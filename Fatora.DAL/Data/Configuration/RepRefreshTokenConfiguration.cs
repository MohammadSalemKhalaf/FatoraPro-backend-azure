using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fatora.DAL.Data.Configuration;

public class RepRefreshTokenConfiguration : IEntityTypeConfiguration<RepRefreshToken>
{
    public void Configure(EntityTypeBuilder<RepRefreshToken> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Token).HasMaxLength(200);

        builder.HasIndex(x => x.Token).IsUnique();

        builder.HasOne(x => x.Rep).WithMany().HasForeignKey(x => x.RepId);
    }
}
