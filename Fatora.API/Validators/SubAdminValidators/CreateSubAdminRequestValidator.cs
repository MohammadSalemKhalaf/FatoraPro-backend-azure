using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators.SubAdminValidators;

public class CreateSubAdminRequestValidator : AbstractValidator<CreateSubAdminRequest>
{
    public CreateSubAdminRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("SubAdmin name can not be empty");
    }
}
