using Fatora.DAL.Entities;

namespace Fatora.DAL.Entites;

/// A general payment against a customer's total debt, not tied to any
/// specific invoice - see OrderService/SyncService for how this differs
/// from Order.PaidAmount. Soft-deleted (IsActive), same as Customer/Product,
/// so offline sync history stays consistent.
public class Receipt : ISyncableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public decimal Amount { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; }
}
