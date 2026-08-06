using Fatora.DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fatora.DAL.Entites;

public class Product : ISyncableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? Barcode { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal SellPrice { get; set; }
    // Null = stock isn't tracked for this product (the Settings toggle is
    // off, or the user never set a quantity). Never validated/clamped -
    // sales must never be blocked by it, so it's allowed to go negative.
    public int? StockQuantity { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; }

    // Only ever set for a product created by a Restricted-mode Rep (see
    // ProductService.CreateAsync) - null for everything the owner created,
    // and for anything created by an All-mode Rep (not offered to them; an
    // All-mode Rep already sees the owner's whole shared catalog and has no
    // need for a product of their own). Mirrors Order/Customer.CreatedByRepId.
    public Guid? CreatedByRepId { get; set; }
    public Rep? CreatedByRep { get; set; }

    public List<OrderItem> Products { get; set; } = new List<OrderItem>();

}
