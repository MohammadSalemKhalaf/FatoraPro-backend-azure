using Fatora.BL.DTOs.Requests;
using FluentValidation;

namespace Fatora.API.Validators.SyncValidators;

/// Validates one CustomerSyncItem in isolation - see SyncController.Push for
/// why this runs per-item instead of as part of SyncPushRequestValidator.
public class CustomerSyncItemValidator : AbstractValidator<CustomerSyncItem>
{
    public CustomerSyncItemValidator()
    {
        RuleFor(c => c.Id).NotEqual(Guid.Empty);
        RuleFor(c => c.Name).NotEmpty();
    }
}
