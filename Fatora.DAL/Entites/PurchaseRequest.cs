namespace Fatora.DAL.Entites;

public class PurchaseRequest
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? CreatedByRepId { get; set; }
    public Rep? CreatedByRep { get; set; }
    public Guid? CustomerId { get; set; }
    public string Status { get; set; } = "draft";
    public string? Notes { get; set; }
    public Guid? InvoiceId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Same purpose as Order.SyncedAt (the rep activity feed's rolling
    // window needs server-arrival time, not the device's own CreatedAt),
    // but unlike Order/Receipt this is stamped on *every* save, not just
    // creation - a request commonly sits as "draft" long before it becomes
    // "preparing"/"ready", and that later transition is the moment the
    // feed actually cares about. See PurchaseRequestsController.Save.
    public DateTime? SyncedAt { get; set; }

    // Captured on the device the first time this request actually becomes
    // real activity - preparing or ready, never a draft (see
    // PurchaseRequestsController.Save) - null when location permission was
    // never granted, the device was offline at that moment, or the request
    // never left draft. Set once and never overwritten by a later
    // edit-save - same "where did this activity begin" semantics as
    // Order.Latitude/Longitude, just anchored to a later moment for this
    // entity specifically.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public List<PurchaseRequestItem> Items { get; set; } = [];
}

public class PurchaseRequestItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PurchaseRequestId { get; set; }
    public PurchaseRequest PurchaseRequest { get; set; } = null!;
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
}
