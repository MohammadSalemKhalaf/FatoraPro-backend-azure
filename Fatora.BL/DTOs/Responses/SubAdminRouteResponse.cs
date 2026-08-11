namespace Fatora.BL.DTOs.Responses;

// Mirrors RepRouteResponse - same idea, applied to a SubAdmin's own
// subscriber-management actions instead of a Rep's Orders/Receipts.
public class SubAdminRouteResponse
{
    public List<SubAdminRoutePointResponse> Points { get; set; } = new();

    // How many of that day's actions had no location to plot (no
    // permission, or offline at the moment of the action).
    public int UnlocatedCount { get; set; }
}

public class SubAdminRoutePointResponse
{
    public Guid Id { get; set; }

    // "Activated", "Suspended", or "Reactivated".
    public string Type { get; set; } = string.Empty;

    // 1-based position in chronological order for that day - what actually
    // gets plotted as the numbered, connected route.
    public int Sequence { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string SubscriberName { get; set; } = string.Empty;

    // The subscription type/duration for an activation; empty for
    // Suspended/Reactivated.
    public string Label { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
