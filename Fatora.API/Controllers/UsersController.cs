using Fatora.API.Validators.UserValidators;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Admin")]
public class UsersController(
    IUserService userService,
    CreateSalesRepRequestValidator createValidator,
    UpdateSubscriptionRequestValidator updateSubscriptionValidator,
    ResetPasswordRequestValidator resetPasswordValidator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetUsers([FromQuery] string? search)
    {
        var result = await userService.GetUsersAsync(search);
        return Ok(result);
    }

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

    [HttpPost("{id:guid}/suspend")]
    public async Task<IActionResult> Suspend(Guid id)
    {
        await userService.SuspendAsync(id);
        return NoContent();
    }

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id)
    {
        await userService.ActivateAsync(id);
        return NoContent();
    }

    [HttpPost("{id:guid}/subscription")]
    public async Task<IActionResult> UpdateSubscription(Guid id, UpdateSubscriptionRequest request)
    {
        var validationResult = await updateSubscriptionValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await userService.UpdateSubscriptionAsync(id, request);
        return Ok(result);
    }

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
