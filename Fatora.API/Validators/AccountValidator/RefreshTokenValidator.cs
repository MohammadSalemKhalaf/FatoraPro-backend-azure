using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators;


public class RefreshTokenValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().WithMessage("RefreshToken can not be empty");
    }
}