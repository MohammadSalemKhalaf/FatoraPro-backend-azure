using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fatora.DAL.Data.Configuration;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.Navigation(x => x.OrderItems).AutoInclude();
        builder.Property(x => x.PaidAmount).HasDefaultValue(0m);
        builder.HasIndex(x => new { x.UserId, x.InvoiceNumber }).IsUnique();
    }
}
