using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators.OrderValidators;

public class UpdateOrderRequestValidator : AbstractValidator<UpdateOrderRequest>
{
    public UpdateOrderRequestValidator()
    {
        RuleFor(x => x.CustomerId).NotEqual(Guid.Empty).WithMessage("A valid CustomerId is required");
        RuleFor(x => x.Discount).InclusiveBetween(0, 100).WithMessage("Discount must be between 0 and 100");
        RuleFor(x => x.CashDiscount).GreaterThanOrEqualTo(0).WithMessage("Cash discount cannot be negative");
        RuleFor(x => x.Items).NotEmpty().WithMessage("An order must have at least one item");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEqual(Guid.Empty).WithMessage("A valid ProductId is required");
            item.RuleFor(i => i.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than 0");
        });
    }
}
