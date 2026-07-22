using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators.UserValidators;

public class UpdateSubscriptionRequestValidator : AbstractValidator<UpdateSubscriptionRequest>
{
    public UpdateSubscriptionRequestValidator()
    {
        RuleFor(x => x.SubscriptionType).IsInEnum().WithMessage("SubscriptionType must be Trial, Monthly, Annual or Lifetime");
    }
}
