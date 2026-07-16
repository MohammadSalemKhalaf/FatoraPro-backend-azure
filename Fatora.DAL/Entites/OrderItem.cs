using Fatora.DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
namespace Fatora.DAL.Entites;

public class OrderItem
{
    public int Id { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    public decimal TotalPrice => UnitPrice * Quantity;

    public Guid OrderId { get; set; }
    public Order Order { get; set; }

    public int ProductId { get; set; }
    public Product Product{ get; set; }
}
