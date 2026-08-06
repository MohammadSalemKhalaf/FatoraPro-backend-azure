namespace Fatora.BL.DTOs.Responses;

public sealed class PendingRepSyncSummaryResponse
{
    public bool HasPendingSync { get; set; }
    public int CustomerCount { get; set; }
    public int ProductCount { get; set; }
    public int OrderCount { get; set; }
    public int ReceiptCount { get; set; }
    public DateTime? CreatedAt { get; set; }
}
