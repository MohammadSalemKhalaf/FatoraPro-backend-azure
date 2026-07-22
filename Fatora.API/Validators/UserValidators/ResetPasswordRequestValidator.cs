using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators.UserValidators;

public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(6).WithMessage("NewPassword must be at least 6 characters");
    }
}
