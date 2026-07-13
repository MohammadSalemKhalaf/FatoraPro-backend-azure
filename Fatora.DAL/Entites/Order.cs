using Fatora.DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fatora.DAL.Entites;

public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public decimal TotalPrice => OrderItems.Sum(o=>o.TotalPrice);

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateOnly DueDate { get; set; }


    public int CustomerId { get; set; }
    public Customer Customer { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; }
    public List<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    

}
