namespace Fatora.BL.DTOs.Responses;

public class ProductResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? Barcode { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal SellPrice { get; set; }
    public int? StockQuantity { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Null for anything the owner (or an All-mode Rep) created - only set
    // for a product a Restricted-mode Rep created itself. The frontend uses
    // this to decide whether to offer edit/delete on a given tile for the
    // currently logged-in Rep.
    public Guid? CreatedByRepId { get; set; }
}
