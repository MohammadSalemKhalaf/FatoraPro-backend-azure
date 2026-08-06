using Fatora.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fatora.DAL.Data.Configuration;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(x => x.UserName).IsRequired().HasColumnType("VARCHAR").HasMaxLength(30);
        builder.Property(x => x.Password).IsRequired();
        builder.Property(x => x.Name).IsRequired();
        builder.Property(x => x.PhoneNumber).IsRequired();
        builder.Property(x => x.City).IsRequired(false);
        builder.Property(x => x.Street).IsRequired(false);
        builder.Property(x => x.BusinessName).IsRequired(false);

        builder.Property(x=>x.Role).HasConversion<string>();
        builder.Property(x => x.SubscriptionType).HasConversion<string>();
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.IsSalesManager).HasDefaultValue(false);
        builder.Property(x => x.NextInvoiceNumber).HasDefaultValue(1);
        builder.Property(x => x.NextInvoiceNumberYear).HasDefaultValue(0);
        builder.HasIndex(x=>x.UserName).IsUnique();
        
    }
}
