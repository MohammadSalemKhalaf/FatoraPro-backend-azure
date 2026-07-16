using System;

namespace Fatora.DAL.Entites;

public class Payment
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }

    public Guid OrderId { get; set; }
    public Order Order { get; set; }
}
