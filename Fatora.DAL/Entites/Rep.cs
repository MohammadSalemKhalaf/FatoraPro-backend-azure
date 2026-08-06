using Fatora.DAL.Entities;

namespace Fatora.DAL.Entites;

public enum AccessMode
{
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

    public AccessMode ProductAccessMode { get; set; } = AccessMode.All;
    public AccessMode CustomerAccessMode { get; set; } = AccessMode.All;

    public DateTime CreatedAt { get; set; }
}
