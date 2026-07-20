namespace Fatora.BL.DTOs.Responses;

public sealed record SyncItemResult(Guid Id, string Status, string? Message = null);

public class SyncPushResponse
{
    public List<SyncItemResult> Customers { get; set; } = new();
    public List<SyncItemResult> Products { get; set; } = new();
    public List<SyncItemResult> Orders { get; set; } = new();
    public List<SyncItemResult> DeletedOrders { get; set; } = new();
}
