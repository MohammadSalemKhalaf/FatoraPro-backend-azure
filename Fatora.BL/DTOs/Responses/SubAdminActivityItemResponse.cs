namespace Fatora.BL.DTOs.Responses;

// One row in the Admin's rolling-24h "Daily Activities" feed for
// SubAdmins - mirrors RepActivityItemResponse. Only ever a SubAdmin-
// performed action (see SubAdminActivityService.GetActivityFeedAsync) -
// the top Admin's own direct actions on a subscriber are excluded, same
// as the Rep feed excludes the owner's own direct Orders/Receipts.
public class SubAdminActivityItemResponse
{
    public Guid Id { get; set; }
    public Guid SubAdminId { get; set; }
    public string SubAdminName { get; set; } = string.Empty;

    // "Activated", "Suspended", or "Reactivated".
    public string Type { get; set; } = string.Empty;

    public string SubscriberName { get; set; } = string.Empty;

    // The raw subscription type ("Monthly"/"Custom"/...) for Activated -
    // same "client already owns the label mapping" reasoning as
    // RepActivityItemResponse.Detail for a PurchaseRequest. Empty for
    // Suspended/Reactivated.
    public string Detail { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    // Null when the device had no location to capture at the moment of
    // the action.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
