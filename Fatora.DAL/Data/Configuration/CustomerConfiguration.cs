using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fatora.DAL.Data.Configuration;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.Property(x => x.Name).IsRequired().HasColumnType("VARCHAR").HasMaxLength(100);
        builder.Property(x => x.PhoneNumber).IsRequired(false);
        builder.Property(x => x.StoreName).IsRequired(false);
        builder.HasIndex(x => new { x.UserId, x.UpdatedAt });
        builder.HasIndex(x => x.CreatedByRepId);

        // See OrderConfiguration.CreatedByRep for why this is Restrict.
        builder.HasOne(x => x.CreatedByRep)
            .WithMany()
            .HasForeignKey(x => x.CreatedByRepId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
