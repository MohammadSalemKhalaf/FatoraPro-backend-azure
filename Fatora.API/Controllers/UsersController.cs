using Fatora.API.Extensions;
using Fatora.API.Validators.UserValidators;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

// Class-level "Admin,SubAdmin" (broadest) with every action narrowed back
// down to "Admin" except UpdateSubscription - multiple [Authorize]
// attributes AND their role sets together rather than the method-level one
// overriding the class-level one, so a narrow class-level restriction can
// never be "widened" by a broader method-level attribute. Same pattern as
// RepsController/SubAdminsController. Only UpdateSubscription accepts a
// SubAdmin caller, per the confirmed "activation-only" permission scope -
// creating subscribers, suspending, and resetting passwords all stay
// Admin-exclusive.
[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Admin,SubAdmin")]
public class UsersController(
    IUserService userService,
    CreateSalesRepRequestValidator createValidator,
    UpdateSubscriptionRequestValidator updateSubscriptionValidator,
    ResetPasswordRequestValidator resetPasswordValidator) : ControllerBase
{
    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> GetUsers([FromQuery] string? search)
    {
        var result = await userService.GetUsersAsync(search);
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

    [Authorize(Roles = "Admin")]
    [HttpPost("{id:guid}/suspend")]
    public async Task<IActionResult> Suspend(Guid id)
    {
        await userService.SuspendAsync(id);
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id)
    {
        await userService.ActivateAsync(id);
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

    [Authorize(Roles = "Admin")]
    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request)
    {
        var validationResult = await resetPasswordValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        await userService.ResetPasswordAsync(id, request.NewPassword);
        return NoContent();
    }
}
