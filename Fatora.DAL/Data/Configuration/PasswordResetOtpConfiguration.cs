using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fatora.DAL.Data.Configuration;

public class PasswordResetOtpConfiguration : IEntityTypeConfiguration<PasswordResetOtp>
{
    public void Configure(EntityTypeBuilder<PasswordResetOtp> builder)
    {
        builder.HasKey(x => x.Id);

        // 64 hex chars of SHA-256 - see PasswordResetOtp.CodeHash.
        builder.Property(x => x.CodeHash).IsRequired().HasMaxLength(64);

        // Lookup is "the one outstanding code for this user", never by code
        // value - AdminRecoveryService has to load the row before it can
        // compare hashes, so it can count the failed attempt against it.
        builder.HasIndex(x => new { x.UserId, x.Used });

        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
    }
}
