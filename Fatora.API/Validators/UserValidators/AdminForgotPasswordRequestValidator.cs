using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators.UserValidators;

public class AdminForgotPasswordRequestValidator : AbstractValidator<AdminForgotPasswordRequest>
{
    public AdminForgotPasswordRequestValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().WithMessage("UserName can not be empty");
    }
}
