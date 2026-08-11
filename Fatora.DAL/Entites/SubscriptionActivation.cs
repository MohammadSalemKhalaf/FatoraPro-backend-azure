using Fatora.DAL.Entities;

namespace Fatora.DAL.Entites;

/// One row per successful subscription activation (Admin or SubAdmin) -
/// UpdateSubscriptionAsync today just overwrites User's subscription fields
/// in place with no history, so this is what makes "last 10 activations per
/// SubAdmin" a real, literal query instead of a fabricated approximation.
/// Doubles as a genuine platform activation audit trail.
public class SubscriptionActivation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public SubscriptionType SubscriptionType { get; set; }
    public int? CustomMonths { get; set; }

    // Null means the top Admin performed this activation directly.
    public Guid? PerformedBySubAdminId { get; set; }
    public SubAdmin? PerformedBySubAdmin { get; set; }

    public DateTime ActivatedAt { get; set; }

    // Captured on the acting Admin/SubAdmin's device at the moment of
    // activation - see Order.Latitude/Longitude, same idea. Null when
    // location permission was never granted or the device was offline.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
