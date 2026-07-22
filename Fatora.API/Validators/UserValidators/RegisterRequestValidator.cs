using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators.UserValidators;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(30).WithMessage("UserName can not be empty and must be at most 30 characters");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password can not be empty");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name can not be empty");
        RuleFor(x => x.PhoneNumber).NotEmpty().WithMessage("PhoneNumber can not be empty");
    }
}
