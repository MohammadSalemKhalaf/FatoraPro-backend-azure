using Fatora.DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fatora.DAL.Entites;

public class Order : ISyncableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string InvoiceNumber { get; set; }
    public decimal Discount { get; set; }
    public decimal CashDiscount { get; set; }
    public string? Notes { get; set; }

    public decimal Subtotal => OrderItems.Sum(o => o.TotalPrice);
    public decimal DiscountAmount => Subtotal * (Discount / 100m);

    // Rounded to the nearest half-unit (e.g. 746.2 -> 746.0, 746.3 -> 746.5) -
    // the payable total is always a clean half or whole amount, never odd cents.
    public decimal Total =>
        Math.Round((Subtotal - DiscountAmount - CashDiscount) * 2, 0, MidpointRounding.AwayFromZero) / 2;
    public decimal PaidAmount { get; set; }
    public decimal RemainingBalance => Total - PaidAmount;

    // Set only by the customer-level "close outstanding invoices" action once
    // their debt is fully covered by general receipts - never by a real
    // payment. Purely additive display metadata: it never changes PaidAmount
    // or the computed Status (see OrderService.ComputeStatus).
    public bool CoveredByReceipt { get; set; }

    // Set whenever a real edit (customer, discount, or line items) changes
    // what this invoice actually says, at which point InvoiceNumber is also
    // reissued - see OrderService.UpdateAsync/SyncService.PushOrderAsync.
    // Purely additive audit metadata: it never hides or replaces anything
    // else on the invoice, it's just visible proof the invoice was edited
    // after its original number was issued.
    public bool IsEdited { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateOnly? DueDate { get; set; }


    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; }
    public List<OrderItem> OrderItems { get; set; } = new List<OrderItem>();

    // Which sub-account (if any) actually created this invoice - UserId
    // above still always resolves to the owning business, unchanged, so
    // every existing report/sync/CSV/PDF path keeps working without knowing
    // Reps exist at all. Null for anything created directly by the owner.
    // Never set on delete/reassignment - see Rep.cs.
    public Guid? CreatedByRepId { get; set; }
    public Rep? CreatedByRep { get; set; }
}
