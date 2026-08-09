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
