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

    // "Order" or "Receipt".
    public string Type { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;

    // The invoice number for an Order, empty for a Receipt (which has no
    // equivalent identifier worth surfacing here).
    public string Detail { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }

    // Null when the device had no location to capture at creation time -
    // see Order.Latitude/Longitude for why.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
