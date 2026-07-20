using Fatora.DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fatora.DAL.Entites;

public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string InvoiceNumber { get; set; }
    public decimal Discount { get; set; }
    public string? Notes { get; set; }

    public decimal Subtotal => OrderItems.Sum(o => o.TotalPrice);
    public decimal DiscountAmount => Subtotal * (Discount / 100m);
    public decimal Total => Subtotal - DiscountAmount;
    public decimal PaidAmount { get; set; }
    public decimal RemainingBalance => Total - PaidAmount;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateOnly DueDate { get; set; }


    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; }
    public List<OrderItem> OrderItems { get; set; } = new List<OrderItem>();

}
