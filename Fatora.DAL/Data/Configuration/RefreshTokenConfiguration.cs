using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fatora.DAL.Data.Configuration;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(x=>x.Id);
        builder.Property(x => x.Token).HasMaxLength(200);

        builder.HasIndex(x => x.Token).IsUnique();

        builder.HasOne(x => x.User).WithMany().HasForeignKey(x=>x.UserId);
    }
}
