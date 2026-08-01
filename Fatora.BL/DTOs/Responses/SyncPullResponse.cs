namespace Fatora.BL.DTOs.Responses;

public class SyncPullResponse
{
    public DateTime ServerTime { get; set; }
    public List<CustomerResponse> Customers { get; set; } = new();
    public List<ProductResponse> Products { get; set; } = new();
    public List<OrderResponse> Orders { get; set; } = new();
    public List<ReceiptResponse> Receipts { get; set; } = new();
}
