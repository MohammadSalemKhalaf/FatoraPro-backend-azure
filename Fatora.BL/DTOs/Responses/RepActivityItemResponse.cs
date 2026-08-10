namespace Fatora.BL.DTOs.Responses;

// One row in the owner's rolling-24h "Daily Activities" feed - see
// RepActivityService.GetActivityFeedAsync. Deliberately lightweight (no
// line items, no full customer record) since this is a monitoring glance,
// not a way to open the underlying invoice/receipt.
public class RepActivityItemResponse
{
    public Guid Id { get; set; }
    public Guid RepId { get; set; }
    public string RepName { get; set; } = string.Empty;

    // "Order", "Receipt", or "PurchaseRequest".
    public string Type { get; set; } = string.Empty;

    // "بدون عميل" for a PurchaseRequest with no customer chosen yet -
    // Order/Receipt always have a real customer.
    public string CustomerName { get; set; } = string.Empty;

    // The invoice number for an Order; the raw status ("preparing"/"ready")
    // for a PurchaseRequest; empty for a Receipt (which has no equivalent
    // identifier worth surfacing here).
    public string Detail { get; set; } = string.Empty;

    // Always 0 for a PurchaseRequest - it has no monetary total, only
    // quantities.
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }

    // Null when the device had no location to capture at creation time -
    // see Order.Latitude/Longitude for why. Always null for a
    // PurchaseRequest - that entity has no location fields at all.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
