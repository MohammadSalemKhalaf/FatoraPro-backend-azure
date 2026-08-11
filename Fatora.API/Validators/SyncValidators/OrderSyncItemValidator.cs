using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators.SyncValidators;

/// Validates one OrderSyncItem in isolation - see SyncController.Push for
/// why this runs per-item instead of as part of SyncPushRequestValidator.
public class OrderSyncItemValidator : AbstractValidator<OrderSyncItem>
{
    public OrderSyncItemValidator()
    {
        RuleFor(o => o.Id).NotEqual(Guid.Empty);
        RuleFor(o => o.CustomerId).NotEqual(Guid.Empty);
        RuleFor(o => o.Discount).InclusiveBetween(0, 100);
        RuleFor(o => o.CashDiscount).GreaterThanOrEqualTo(0);
        RuleFor(o => o.PaidAmount).GreaterThanOrEqualTo(0);
        RuleFor(o => o.Items).NotEmpty();

        RuleForEach(o => o.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEqual(Guid.Empty);
            item.RuleFor(i => i.Quantity).GreaterThan(0);
            item.RuleFor(i => i.UnitPrice).GreaterThan(0);
        });
    }
}
