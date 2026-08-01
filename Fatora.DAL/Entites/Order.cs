using Fatora.DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fatora.DAL.Entites;

public class Order : ISyncableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string InvoiceNumber { get; set; }
    public decimal Discount { get; set; }
    public decimal CashDiscount { get; set; }
    public string? Notes { get; set; }

    public decimal Subtotal => OrderItems.Sum(o => o.TotalPrice);
    public decimal DiscountAmount => Subtotal * (Discount / 100m);

    // Rounded to the nearest half-unit (e.g. 746.2 -> 746.0, 746.3 -> 746.5) -
    // the payable total is always a clean half or whole amount, never odd cents.
    public decimal Total =>
        Math.Round((Subtotal - DiscountAmount - CashDiscount) * 2, 0, MidpointRounding.AwayFromZero) / 2;
    public decimal PaidAmount { get; set; }
    public decimal RemainingBalance => Total - PaidAmount;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateOnly? DueDate { get; set; }


    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; }
    public List<OrderItem> OrderItems { get; set; } = new List<OrderItem>();

}
