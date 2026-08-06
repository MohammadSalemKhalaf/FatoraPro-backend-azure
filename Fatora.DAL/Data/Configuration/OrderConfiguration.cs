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
        builder.Property(x => x.CashDiscount).HasDefaultValue(0m);
        builder.HasIndex(x => new { x.UserId, x.InvoiceNumber }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.UpdatedAt });
        builder.HasIndex(x => x.CreatedByRepId);

        // Restrict, not cascade: a Rep is only ever soft-deactivated (see
        // Rep.cs), never actually removed, so this never fires in practice -
        // but it documents that historical invoices must keep resolving
        // even if that ever changes.
        builder.HasOne(x => x.CreatedByRep)
            .WithMany()
            .HasForeignKey(x => x.CreatedByRepId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
