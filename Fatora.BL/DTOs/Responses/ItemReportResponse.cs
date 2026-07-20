namespace Fatora.BL.DTOs.Responses;

public class ItemReportResponse
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ProductImageUrl { get; set; }
    public int QuantitySold { get; set; }
    public decimal TotalValue { get; set; }
}
