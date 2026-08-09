namespace Fatora.BL.DTOs.Responses;

public class ReceiptResponse
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public decimal Amount { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Null for anything the owner created - see ProductResponse's identical
    // field for why this needs to reach the client at all (rep-scoped local
    // filtering for the owner's own "filter by rep" view).
    public Guid? CreatedByRepId { get; set; }

    // Null when location permission was never granted or the device was
    // offline at creation time - see Receipt.Latitude/Longitude.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
