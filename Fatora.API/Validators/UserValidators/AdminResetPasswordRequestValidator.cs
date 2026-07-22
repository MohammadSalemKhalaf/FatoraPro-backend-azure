using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators.UserValidators;

public class AdminResetPasswordRequestValidator : AbstractValidator<AdminResetPasswordRequest>
{
    public AdminResetPasswordRequestValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().WithMessage("UserName can not be empty");
        RuleFor(x => x.Otp).NotEmpty().Length(6).WithMessage("Otp must be a 6-digit code");
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(6).WithMessage("NewPassword must be at least 6 characters");
    }
}
