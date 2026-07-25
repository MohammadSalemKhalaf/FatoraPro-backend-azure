using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators.ProductValidators;

public class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Product name can not be empty");
        RuleFor(x => x.PurchasePrice).GreaterThanOrEqualTo(0).WithMessage("Purchase price can not be negative");
        RuleFor(x => x.SellPrice).GreaterThanOrEqualTo(0).WithMessage("Sell price can not be negative");
        RuleFor(x => x.Barcode).MaximumLength(64).WithMessage("Barcode is too long");
    }
}
