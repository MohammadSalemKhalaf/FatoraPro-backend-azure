using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators.RepValidators;

public class CreateRepRequestValidator : AbstractValidator<CreateRepRequest>
{
    public CreateRepRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Rep name can not be empty");
    }
}
