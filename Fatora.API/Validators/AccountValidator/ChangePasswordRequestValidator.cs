using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators;

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("CurrentPassword can not be empty");
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(6).WithMessage("NewPassword must be at least 6 characters");
    }
}
