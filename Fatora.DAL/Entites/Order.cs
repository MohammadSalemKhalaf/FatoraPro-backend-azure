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

    // A real, stored column (not computed) - true decimal precision, no
    // rounding. Explicitly (re)assigned by OrderService.ComputeTotal at the
    // exact moments an order is genuinely created or edited
    // (OrderService.CreateAsync/UpdateAsync, SyncService.PushOrderAsync's
    // create/edit branches) - never touched by RecordPaymentAsync/
    // ReturnAsync, which only read it. This is what lets an already-issued
    // invoice's Total stay frozen forever once set, instead of drifting if
    // the formula (or, previously, its rounding rule) ever changes -
    // existing rows keep whatever value the AddOrderStoredTotal migration
    // backfilled them with under the old half-unit-rounded formula.
    public decimal Total { get; set; }
    public decimal PaidAmount { get; set; }

    // A returned invoice contributes nothing to what the customer still
    // owes - PaidAmount itself is deliberately left untouched (real cash
    // already collected stays an honest historical record; see
    // OrderService.ReturnAsync), it's only the *remaining* balance that
    // clears. This is a distinct concept from CoveredByReceipt below - an
    // administrative write-off - not a reuse of it.
    public decimal RemainingBalance => IsReturned ? 0 : Total - PaidAmount;

    // Set only by the customer-level "close outstanding invoices" action once
    // their debt is fully covered by general receipts - never by a real
    // payment. Purely additive display metadata: it never changes PaidAmount
    // or the computed Status (see OrderService.ComputeStatus).
    public bool CoveredByReceipt { get; set; }

    // Set only by OrderService.ReturnAsync (goods physically returned) -
    // restores each line's stock quantity once, permanently excludes this
    // invoice's Total from sales reporting, and clears RemainingBalance
    // above. Never set anywhere else; never unset.
    public bool IsReturned { get; set; }

    // Set whenever a real edit (customer, discount, or line items) changes
    // what this invoice actually says, at which point InvoiceNumber is also
    // reissued - see OrderService.UpdateAsync/SyncService.PushOrderAsync.
    // Purely additive audit metadata: it never hides or replaces anything
    // else on the invoice, it's just visible proof the invoice was edited
    // after its original number was issued.
    public bool IsEdited { get; set; }

    // Set only by OrderService.DeleteAsync - a purely operational "hide from
    // the Invoices list" flag, deliberately with no side effects of its own:
    // no stock restore, no change to Status/RemainingBalance, nothing
    // excluded from reports, the customer statement, or CSV exports. Those
    // all keep reading this order exactly as if it were never deleted; only
    // the Invoices list filters it out. Distinct from IsReturned, which is a
    // real financial state change - deleting is expected to normally follow
    // a return, but isn't required to.
    public bool IsDeleted { get; set; }

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

    // Captured on the device at the moment this invoice was created (see
    // SyncService.PushOrderAsync) - null when location permission was never
    // granted or the device was offline at that moment. Set once, at
    // creation, and never overwritten by a later edit-sync.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // When this row actually reached the server via a rep's sync push (see
    // SyncService.PushOrderAsync) - deliberately separate from CreatedAt,
    // which is stamped from the device's own clock and can be hours behind
    // if the rep worked offline for a while. RepActivityService's rolling
    // "recent activity" window needs the former, everything else (reports,
    // display, last-write-wins conflict resolution) needs the latter. Set
    // once, at creation, and never overwritten by a later edit-sync. Null
    // for anything created directly by the owner (never pushed through
    // sync) or synced before this column existed.
    public DateTime? SyncedAt { get; set; }
}
