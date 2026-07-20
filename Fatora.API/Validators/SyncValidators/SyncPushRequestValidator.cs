using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators.SyncValidators;

public class SyncPushRequestValidator : AbstractValidator<SyncPushRequest>
{
    public SyncPushRequestValidator()
    {
        RuleForEach(x => x.Customers).ChildRules(customer =>
        {
            customer.RuleFor(c => c.Id).NotEqual(Guid.Empty);
            customer.RuleFor(c => c.Name).NotEmpty();
            customer.RuleFor(c => c.PhoneNumber).NotEmpty();
            customer.RuleFor(c => c.Street).NotEmpty();
            customer.RuleFor(c => c.City).NotEmpty();
        });

        RuleForEach(x => x.Products).ChildRules(product =>
        {
            product.RuleFor(p => p.Id).NotEqual(Guid.Empty);
            product.RuleFor(p => p.Name).NotEmpty();
            product.RuleFor(p => p.SellPrice).GreaterThanOrEqualTo(0);
            product.RuleFor(p => p.PurchasePrice).GreaterThanOrEqualTo(0);
        });

        RuleForEach(x => x.Orders).ChildRules(order =>
        {
            order.RuleFor(o => o.Id).NotEqual(Guid.Empty);
            order.RuleFor(o => o.CustomerId).NotEqual(Guid.Empty);
            order.RuleFor(o => o.Discount).InclusiveBetween(0, 100);
            order.RuleFor(o => o.PaidAmount).GreaterThanOrEqualTo(0);
            order.RuleFor(o => o.Items).NotEmpty();

            order.RuleForEach(o => o.Items).ChildRules(item =>
            {
                item.RuleFor(i => i.ProductId).NotEqual(Guid.Empty);
                item.RuleFor(i => i.Quantity).GreaterThan(0);
                item.RuleFor(i => i.UnitPrice).GreaterThan(0);
            });
        });
    }
}
