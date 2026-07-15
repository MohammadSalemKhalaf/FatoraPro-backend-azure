using Fatora.DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fatora.DAL.Entites;

public class Product
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal SellPrice { get; set; }
    public bool IsActive { get; set; } = true;

    public Guid UserId { get; set; }
    public User User { get; set; }

    public List<OrderItem> Products { get; set; } = new List<OrderItem>();
  
}
