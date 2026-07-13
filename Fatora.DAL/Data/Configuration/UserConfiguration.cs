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
        builder.Property(x => x.City).IsRequired();
        builder.Property(x => x.Street).IsRequired();
        builder.Property(x => x.BusinessName).IsRequired(false);

        
        
    }
}
