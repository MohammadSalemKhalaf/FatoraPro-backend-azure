using Fatora.BL.DTOs.Requests;
using Fatora.DAL.Entities;
using FluentValidation;

namespace Fatora.API.Validators.UserValidators;

public class UpdateSubscriptionRequestValidator : AbstractValidator<UpdateSubscriptionRequest>
{
    public UpdateSubscriptionRequestValidator()
    {
        RuleFor(x => x.SubscriptionType).IsInEnum().WithMessage("SubscriptionType must be Trial, Monthly, Annual, Lifetime or Custom");

        RuleFor(x => x.CustomMonths)
            .NotNull().WithMessage("CustomMonths must be a positive number when SubscriptionType is Custom")
            .GreaterThan(0).WithMessage("CustomMonths must be a positive number when SubscriptionType is Custom")
            .When(x => x.SubscriptionType == SubscriptionType.Custom);

        RuleFor(x => x.CustomMonths)
            .Null()
            .When(x => x.SubscriptionType != SubscriptionType.Custom)
            .WithMessage("CustomMonths is only valid when SubscriptionType is Custom");
    }
}
