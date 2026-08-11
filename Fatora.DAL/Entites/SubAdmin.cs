namespace Fatora.DAL.Entites;

/// A QR-linked sub-account under the platform's single top-level Admin -
/// same idea as Rep (a SalesRep-tier owner's own sub-accounts), one tier up.
/// Unlike Rep, it has no OwnerUserId: there's exactly one Admin account
/// platform-wide, so a SubAdmin belongs to the platform directly rather than
/// to a specific owner row. Logs in only via QrToken, never a
/// username/password. Permanent once created: only IsHidden (via
/// SubAdminService.DeleteAsync) and IsActive (via DeactivateAsync) change
/// its lifecycle - every subscription activation it ever performed keeps
/// pointing at it regardless (see SubscriptionActivation.PerformedBySubAdminId
/// and User.ManagedBySubAdminId), never affected by deleting/deactivating it.
public class SubAdmin
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Name { get; set; }

    // Presented as a QR code, re-scannable indefinitely - stored as-is, not
    // hashed, same reasoning as Rep.QrToken.
    public required string QrToken { get; set; }

    public bool IsActive { get; set; } = true;

    // Set only by SubAdminService.DeleteAsync - hides this SubAdmin from the
    // Admin's management list without touching any subscriber it ever
    // claimed or activated a subscription for. A superset of deactivation
    // (also sets IsActive = false), never undone. See Rep.IsHidden for the
    // identical pattern.
    public bool IsHidden { get; set; } = false;

    // Bumped by SubAdminService.LogoutAsync/DeactivateAsync/DeleteAsync,
    // embedded as a claim at token-issue time - lets AccountStatusFilter
    // reject an already-issued access token the instant it's stale. Same
    // mechanism as Rep.SessionVersion.
    public int SessionVersion { get; set; }

    // Which subscription durations this SubAdmin may activate for a
    // subscriber - the only permission surface it has (see
    // UserService.UpdateSubscriptionAsync's permission gate). The top Admin
    // always bypasses these checks entirely.
    public bool CanActivateMonthly { get; set; }
    public bool CanActivateAnnual { get; set; }
    public bool CanActivateCustomMonths { get; set; }

    // Suspending/reactivating a subscriber's account - see
    // UserService.EnsureSubAdminCanManageAccountAsync. Independent of
    // CanResetPassword below: the Admin may grant either, both, or neither.
    public bool CanManageAccount { get; set; }

    // Resetting a subscriber's password - see
    // UserService.EnsureSubAdminCanResetPasswordAsync. Split out from
    // CanManageAccount rather than folded into it, since the Admin must be
    // able to grant one without the other.
    public bool CanResetPassword { get; set; }

    public DateTime CreatedAt { get; set; }
}
