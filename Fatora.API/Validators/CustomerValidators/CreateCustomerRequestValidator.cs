using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators.CustomerValidators;


public class CreateCustomerRequestValidator : AbstractValidator<CreateCustomerRequest>
{
    public CreateCustomerRequestValidator()
    {
        RuleFor(x=>x.Name).NotEmpty().WithMessage("customer Name can not be empty");
        RuleFor(x=>x.PhoneNumber).NotEmpty().WithMessage("customer Phone number can not be empty");
    }
}