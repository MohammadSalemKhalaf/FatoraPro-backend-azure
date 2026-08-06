using Fatora.DAL.Entities;

namespace Fatora.DAL.Entites;

public enum AccessMode
{
    All,
    Restricted
}

// Distinct from AccessMode (Product) because a Product's "All" is
// genuinely just the whole shared catalog with no per-Rep default to speak
// of - a Restricted Rep additionally sees whatever it created itself (see
// Product.CreatedByRepId), same idea as Customer below, but there's no
// third "Own" default state the way Customer has, since every Rep started
// out with All-mode catalog access, never an empty one of their own.
// Customers ARE rep-owned (CreatedByRepId) with a real historical default,
// so a third state is meaningful there: Own is every Rep's actual behavior
// before this mode existed at all (each sub-account's customer book starts
// empty and stays its own), All opens up the owner's entire shared customer
// book, Restricted hands a Rep an admin-picked subset of it (via
// RepCustomerAccess) while still always letting it see whatever it
// personally created itself.
public enum CustomerAccessMode
{
    Own,
    All,
    Restricted
}

/// A sub-account under a SalesRep-tier User ("مدير مبيعات" once they've
/// opted into IsSalesManager) - logs in only via QrToken, never a
/// username/password. Permanent once created: logging out or losing a
/// device never deletes it, only IsActive = false (via RepService.
/// DeactivateAsync) does, and even then every Order/Customer/Receipt it
/// created keeps pointing at it for the owner's own history/filtering -
/// see CreatedByRepId on those entities.
public class Rep
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OwnerUserId { get; set; }
    public User OwnerUser { get; set; }

    public required string Name { get; set; }

    // Presented as a QR code and re-scannable indefinitely (not a one-time
    // pairing code) - stored as-is, not hashed, since it has to be
    // re-presentable on demand rather than consumed once like a refresh
    // token. RepService.RegenerateQrAsync issuing a new one is how an old
    // QR image gets invalidated without touching the rep's data.
    public required string QrToken { get; set; }

    public bool IsActive { get; set; } = true;

    // Bumped by RepService.LogoutAsync/DeactivateAsync, embedded as a claim
    // at token-issue time (see RepAuthService.GenerateTokenAsync) - lets
    // AccountStatusFilter reject an already-issued access token the instant
    // it's stale, the same way IsActive already does for a deactivated rep.
    // Needed because a JWT is otherwise stateless: revoking the refresh
    // token (what "logout" alone used to do) only stops a *future* refresh,
    // never invalidates a still-live access token already in the app's
    // hands.
    public int SessionVersion { get; set; }

    public AccessMode ProductAccessMode { get; set; } = AccessMode.All;
    public CustomerAccessMode CustomerAccessMode { get; set; } = CustomerAccessMode.Own;

    public DateTime CreatedAt { get; set; }
}
