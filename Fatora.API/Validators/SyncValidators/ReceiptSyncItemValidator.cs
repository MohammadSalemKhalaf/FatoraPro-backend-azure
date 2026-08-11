using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators.SyncValidators;

/// Validates one ReceiptSyncItem in isolation - see SyncController.Push for
/// why this runs per-item instead of as part of SyncPushRequestValidator.
public class ReceiptSyncItemValidator : AbstractValidator<ReceiptSyncItem>
{
    public ReceiptSyncItemValidator()
    {
        RuleFor(r => r.Id).NotEqual(Guid.Empty);
        RuleFor(r => r.CustomerId).NotEqual(Guid.Empty);
        RuleFor(r => r.Amount).GreaterThan(0);
    }
}
