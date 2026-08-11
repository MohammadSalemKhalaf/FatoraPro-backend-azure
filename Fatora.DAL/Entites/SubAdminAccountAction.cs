using Fatora.DAL.Entities;

namespace Fatora.DAL.Entites;

/// One row per suspend/reactivate performed on a subscriber's account -
/// UserService.SuspendAsync/ActivateAsync previously left no history at
/// all. Feeds the same SubAdmin daily-activity-feed/route views as
/// SubscriptionActivation (which already covers the third action,
/// activation, on its own dedicated row shape).
public class SubAdminAccountAction
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public SubAdminAccountActionType ActionType { get; set; }

    // Null means the top Admin performed this action directly.
    public Guid? PerformedBySubAdminId { get; set; }
    public SubAdmin? PerformedBySubAdmin { get; set; }

    public DateTime CreatedAt { get; set; }

    // Captured on the acting Admin/SubAdmin's device at the moment of the
    // action - see Order.Latitude/Longitude, same idea. Null when location
    // permission was never granted or the device was offline.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
