using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators.OrderValidators;

public class RecordPaymentRequestValidator : AbstractValidator<RecordPaymentRequest>
{
    public RecordPaymentRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Payment amount must be greater than 0");
    }
}
