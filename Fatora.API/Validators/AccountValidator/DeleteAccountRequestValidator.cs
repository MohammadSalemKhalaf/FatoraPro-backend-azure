using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators;

public class DeleteAccountRequestValidator : AbstractValidator<DeleteAccountRequest>
{
    public DeleteAccountRequestValidator()
    {
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password can not be empty");
    }
}
