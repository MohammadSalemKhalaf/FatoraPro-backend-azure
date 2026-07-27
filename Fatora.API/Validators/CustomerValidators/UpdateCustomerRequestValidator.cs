using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators.CustomerValidators;

public class UpdateCustomerRequestValidator : AbstractValidator<UpdateCustomerRequest>
{
    public UpdateCustomerRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("customer Name can not be empty");
    }
}
