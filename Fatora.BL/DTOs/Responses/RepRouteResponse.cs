namespace Fatora.BL.DTOs.Responses;

public class RepRouteResponse
{
    public List<RepRoutePointResponse> Points { get; set; } = new();

    // How many of that day's Orders/Receipts had no location to plot (no
    // permission, or offline at creation time) - lets the UI show "N records
    // had no location that day" instead of silently dropping them.
    public int UnlocatedCount { get; set; }
}

public class RepRoutePointResponse
{
    public Guid Id { get; set; }

    // "Order" or "Receipt" - the only two record types this feature knows
    // about.
    public string Type { get; set; } = string.Empty;

    // 1-based position in chronological order for that day - what actually
    // gets plotted as the numbered, connected route.
    public int Sequence { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string CustomerName { get; set; } = string.Empty;

    // The invoice number for an Order, the formatted amount for a Receipt -
    // whatever is most useful to show next to that pin.
    public string Label { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
