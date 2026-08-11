using Fatora.API.Extensions;
using Fatora.API.Validators.UserValidators;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

// Class-level "Admin,SubAdmin" (broadest) with every action narrowed back
// down to "Admin" except GetUsers, UpdateSubscription, Suspend, Activate,
// and ResetPassword - multiple [Authorize] attributes AND their role sets
// together rather than the method-level one overriding the class-level one,
// so a narrow class-level restriction can never be "widened" by a broader
// method-level attribute. Same pattern as RepsController/SubAdminsController.
// Creating subscribers stays Admin-exclusive. GetUsers is safe to open up
// because it auto-scopes a SubAdmin caller to its own claimed subscribers
// (see GetUsersAsync); UpdateSubscription is gated per-call by that
// SubAdmin's own granted CanActivate* flags; Suspend/Activate are gated by
// its CanManageAccount flag and ResetPassword by its separate
// CanResetPassword flag (see UserService.EnsureSubAdminCanManageAccountAsync
// / EnsureSubAdminCanResetPasswordAsync) - independently grantable.
[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Admin,SubAdmin")]
public class UsersController(
    IUserService userService,
    CreateSalesRepRequestValidator createValidator,
    UpdateSubscriptionRequestValidator updateSubscriptionValidator,
    ResetPasswordRequestValidator resetPasswordValidator) : ControllerBase
{
    // A SubAdmin session can call this too (see the class-level Authorize) -
    // GetUsersAsync forces the result down to its own claimed subscribers
    // regardless of the subAdminId query param in that case, so this stays
    // safe to open up rather than needing a separate, near-duplicate
    // SubAdmin-only endpoint.
    [Authorize(Roles = "Admin,SubAdmin")]
    [HttpGet]
    public async Task<IActionResult> GetUsers([FromQuery] string? search, [FromQuery] Guid? subAdminId)
    {
        var result = await userService.GetUsersAsync(search, subAdminId, User.GetSubAdminIdOrNull());
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("sales-reps")]
    public async Task<IActionResult> CreateSalesRep(CreateSalesRepRequest request)
    {
        var validationResult = await createValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await userService.CreateSalesRepAsync(request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [Authorize(Roles = "Admin,SubAdmin")]
    [HttpPost("{id:guid}/suspend")]
    public async Task<IActionResult> Suspend(Guid id, LocationRequest? location = null)
    {
        await userService.SuspendAsync(id, User.GetSubAdminIdOrNull(), location);
        return NoContent();
    }

    [Authorize(Roles = "Admin,SubAdmin")]
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, LocationRequest? location = null)
    {
        await userService.ActivateAsync(id, User.GetSubAdminIdOrNull(), location);
        return NoContent();
    }

    [Authorize(Roles = "Admin,SubAdmin")]
    [HttpPost("{id:guid}/subscription")]
    public async Task<IActionResult> UpdateSubscription(Guid id, UpdateSubscriptionRequest request)
    {
        var validationResult = await updateSubscriptionValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await userService.UpdateSubscriptionAsync(id, request, User.GetSubAdminIdOrNull());
        return Ok(result);
    }

    [Authorize(Roles = "Admin,SubAdmin")]
    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request)
    {
        var validationResult = await resetPasswordValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        await userService.ResetPasswordAsync(id, request.NewPassword, User.GetSubAdminIdOrNull());
        return NoContent();
    }
}
