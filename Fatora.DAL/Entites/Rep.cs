using Fatora.DAL.Entities;

namespace Fatora.DAL.Entites;

// Three deliberately exclusive modes - a Rep is always in exactly one, never
// a blend:
// - RepOwnedOnly: starts with an empty catalog; the Rep creates its own
//   products (see Product.CreatedByRepId) and sees only those. The owner
//   can later grant a RepOwnedOnly-created product to some other Rep via
//   the normal RepProductAccess assignment flow (SetProductAccessAsync) -
//   nothing special needed for that, since the product is a completely
//   ordinary company-owned row already.
// - Selected: sees only an owner-curated subset via RepProductAccess.
//   Can never create a product of its own.
// - All: sees (and, unlike before, can also create into) the owner's whole
//   shared catalog - a product it creates is exactly as "company-owned" as
//   one any other Rep in All mode already sees.
public enum ProductAccessMode
{
    RepOwnedOnly,
    Selected,
    All
}

// Distinct from ProductAccessMode because Customers ARE rep-owned
// (CreatedByRepId) with a real historical default, so a third state is
// meaningful there: Own is every Rep's actual behavior before this mode
// existed at all (each sub-account's customer book starts empty and stays
// its own), All opens up the owner's entire shared customer book,
// Restricted hands a Rep an admin-picked subset of it (via
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

    // Set only by RepService.DeleteAsync (a superset of deactivation - it
    // also sets IsActive = false and bumps SessionVersion, since a hidden
    // rep must never keep working) - hides this rep from the owner's
    // management list (RepService.GetAllAsync's default) without touching
    // any Order/Customer/Product/Receipt it created, which keep resolving
    // and stay filterable exactly like before (see CreatedByRepId on those
    // entities and GetAllAsync's includeHidden param). Once nothing
    // references it any more, AnnualInventoryService's post-wipe sweep hard-
    // deletes it for real - see HardDeleteOrphanedHiddenRepsAsync.
    public bool IsHidden { get; set; } = false;

    // Bumped by RepService.LogoutAsync/DeactivateAsync, embedded as a claim
    // at token-issue time (see RepAuthService.GenerateTokenAsync) - lets
    // AccountStatusFilter reject an already-issued access token the instant
    // it's stale, the same way IsActive already does for a deactivated rep.
    // Needed because a JWT is otherwise stateless: revoking the refresh
    // token (what "logout" alone used to do) only stops a *future* refresh,
    // never invalidates a still-live access token already in the app's
    // hands.
    public int SessionVersion { get; set; }

    public ProductAccessMode ProductAccessMode { get; set; } = ProductAccessMode.All;
    public CustomerAccessMode CustomerAccessMode { get; set; } = CustomerAccessMode.Own;

    // Independent of ProductAccessMode - that controls which products a Rep
    // can even see/touch at all, this controls one narrower thing within
    // that: whether it may change a touchable product's StockQuantity, or
    // only view it. Defaults true (today's behavior for every existing Rep)
    // so this migration changes nothing until an owner deliberately turns it
    // off. Enforced in ProductService.UpdateAsync and
    // SyncService.PushProductAsync - never just a UI toggle.
    public bool CanEditStock { get; set; } = true;

    public DateTime CreatedAt { get; set; }
}
