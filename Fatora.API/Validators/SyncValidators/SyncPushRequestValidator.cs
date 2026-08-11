using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators.SyncValidators;

/// Only the whole-request shape rules live here now - a batch that's simply
/// too large is a legitimate reason to reject the entire request outright.
/// Per-item data-quality rules (a bad name, a zero price, ...) moved to the
/// dedicated CustomerSyncItemValidator/ProductSyncItemValidator/
/// OrderSyncItemValidator/ReceiptSyncItemValidator, validated and filtered
/// per item in SyncController.Push - one malformed row must not block every
/// *other* row bundled in the same offline batch, which for a rep syncing
/// after hours offline can be everything they did all day.
public class SyncPushRequestValidator : AbstractValidator<SyncPushRequest>
{
    // A single sync batch is expected to hold a rep's pending offline work (typically tens of
    // records) - not the whole database. This bounds request size/time without ever being a real
    // limit for the intended usage.
    private const int MaxItemsPerBatch = 500;

    public SyncPushRequestValidator()
    {
        RuleFor(x => x).Must(HaveAReasonableBatchSize)
            .WithMessage($"A single sync batch cannot exceed {MaxItemsPerBatch} total items. Split into multiple pushes.");
    }

    private static bool HaveAReasonableBatchSize(SyncPushRequest request)
    {
        var total = request.Customers.Count
            + request.Products.Count
            + request.Orders.Count
            + request.Orders.Sum(o => o.Items.Count)
            + request.Receipts.Count;

        return total <= MaxItemsPerBatch;
    }
}
