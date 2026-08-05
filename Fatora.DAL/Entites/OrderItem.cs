using Fatora.DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
namespace Fatora.DAL.Entites;

public class OrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    // True when this line was added, or had its quantity/price changed,
    // by an edit after the invoice's original creation - see Order.IsEdited
    // for the same idea at the whole-invoice level. Never true for an item
    // that was already on the invoice and is untouched by the current save.
    public bool IsEdited { get; set; }

    public decimal TotalPrice => UnitPrice * Quantity;

    public Guid OrderId { get; set; }
    public Order Order { get; set; }

    public Guid ProductId { get; set; }
    public Product Product{ get; set; }
}
