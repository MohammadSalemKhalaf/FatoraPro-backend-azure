using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators.SupportContactValidators;

public class CreateSupportContactRequestValidator : AbstractValidator<CreateSupportContactRequest>
{
    public CreateSupportContactRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum().WithMessage("Type must be Phone, WhatsApp or Email");
        RuleFor(x => x.Value).NotEmpty().WithMessage("Value can not be empty").MaximumLength(200);
        RuleFor(x => x.Label).MaximumLength(100);
    }
}
