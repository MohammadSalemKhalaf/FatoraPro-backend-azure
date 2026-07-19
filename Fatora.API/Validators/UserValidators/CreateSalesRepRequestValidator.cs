using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators.UserValidators;

public class CreateSalesRepRequestValidator : AbstractValidator<CreateSalesRepRequest>
{
    public CreateSalesRepRequestValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(30).WithMessage("UserName can not be empty and must be at most 30 characters");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password can not be empty");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name can not be empty");
        RuleFor(x => x.PhoneNumber).NotEmpty().WithMessage("PhoneNumber can not be empty");
        RuleFor(x => x.City).NotEmpty().WithMessage("City can not be empty");
        RuleFor(x => x.Street).NotEmpty().WithMessage("Street can not be empty");
    }
}
