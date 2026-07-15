namespace Fatora.BL.DTOs.Responses;

public class ProductResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal PurchasePrice { get; set; }
    public decimal SellPrice { get; set; }
    public bool IsActive { get; set; }
}
