using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fatora.DAL.Data.Configuration;

public class ItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.Navigation(x => x.Product).AutoInclude();
        builder.Ignore(x => x.TotalPrice);

        // Restrict, not the convention's Cascade: an invoice line is the
        // record of something that was actually sold, so deleting the
        // catalogue entry it points at must fail loudly rather than quietly
        // erase the line - an invoice would otherwise keep its stored Total
        // while showing no items at all. ProductService.PermanentDeleteAsync
        // checks for this up front and returns a readable 409; this is the
        // database-level backstop for every other path that ever removes a
        // Product. The Order side above stays Cascade on purpose - deleting an
        // invoice genuinely should take its own lines with it.
        builder.HasOne(x => x.Product)
            .WithMany(x => x.Products)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
