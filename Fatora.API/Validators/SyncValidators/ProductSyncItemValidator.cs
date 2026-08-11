using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators.SyncValidators;

/// Validates one ProductSyncItem in isolation - see SyncController.Push for
/// why this runs per-item instead of as part of SyncPushRequestValidator.
public class ProductSyncItemValidator : AbstractValidator<ProductSyncItem>
{
    public ProductSyncItemValidator()
    {
        RuleFor(p => p.Id).NotEqual(Guid.Empty);
        RuleFor(p => p.Name).NotEmpty();
        RuleFor(p => p.SellPrice).GreaterThanOrEqualTo(0);
        RuleFor(p => p.PurchasePrice).GreaterThanOrEqualTo(0);
    }
}
