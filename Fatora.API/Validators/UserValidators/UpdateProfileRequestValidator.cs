using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators.UserValidators;

public class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name can not be empty");
        RuleFor(x => x.PhoneNumber).NotEmpty().WithMessage("PhoneNumber can not be empty");
    }
}
