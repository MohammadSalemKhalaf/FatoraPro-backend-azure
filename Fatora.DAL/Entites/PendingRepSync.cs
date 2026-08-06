namespace Fatora.DAL.Entites;

/// A rep's queued-but-not-yet-applied offline sync batch, created only
/// through the narrow post-deactivation upload path (see the RepPendingSync
/// role / AccountStatusFilter's exception for it) - never touched by a
/// normal, still-active Rep session's regular sync push. At most one row per
/// Rep (RepId is unique-indexed): a later upload from the same rep replaces
/// whatever was queued before, since it represents that device's full
/// current dirty set, not an incremental delta.
public class PendingRepSync
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RepId { get; set; }
    public Rep Rep { get; set; }

    // The raw SyncPushRequest JSON, applied later through the exact same
    // SyncService.PushAsync a live Rep session's own sync already goes
    // through - see RepsController's apply endpoint. Kept as opaque JSON
    // rather than normalized rows since it's a write-once,
    // apply-or-discard-once blob, never queried piecemeal.
    public required string PayloadJson { get; set; }

    // Precomputed at upload time purely so the owner's review screen can
    // show "12 items pending" without deserializing PayloadJson on every
    // GET.
    public int CustomerCount { get; set; }
    public int ProductCount { get; set; }
    public int OrderCount { get; set; }
    public int ReceiptCount { get; set; }

    public DateTime CreatedAt { get; set; }
}
