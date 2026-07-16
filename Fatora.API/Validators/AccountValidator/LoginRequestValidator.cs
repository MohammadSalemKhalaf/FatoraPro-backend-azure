using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators;


public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x=>x.UserName).NotEmpty().WithMessage("UserName can not be empty");
        RuleFor(x=>x.Password).NotEmpty().WithMessage("Password can not be empty");
        
    }
}