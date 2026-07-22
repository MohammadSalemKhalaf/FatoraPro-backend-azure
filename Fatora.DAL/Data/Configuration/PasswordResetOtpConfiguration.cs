using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fatora.DAL.Data.Configuration;

public class PasswordResetOtpConfiguration : IEntityTypeConfiguration<PasswordResetOtp>
{
    public void Configure(EntityTypeBuilder<PasswordResetOtp> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).IsRequired().HasMaxLength(6);

        builder.HasIndex(x => new { x.UserId, x.Code });

        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
    }
}
