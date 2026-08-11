using Fatora.API.Extensions;
using Fatora.API.Validators.SubAdminValidators;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

// Class-level "Admin,SubAdmin" (broadest) with every management action
// narrowed back down to "Admin" - multiple [Authorize] attributes AND their
// role sets together rather than the method-level one overriding the
// class-level one, so a narrow class-level restriction can never be
// "widened" by a broader method-level attribute. Same pattern as
// RepsController.
[Route("api/subadmins")]
[ApiController]
[Authorize(Roles = "Admin,SubAdmin")]
public class SubAdminsController(
    ISubAdminService subAdminService,
    ISubAdminAuthService subAdminAuthService,
    IUserService userService,
    ISubAdminActivityService subAdminActivityService,
    CreateSubAdminRequestValidator createSubAdminValidator) : ControllerBase
{
    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateSubAdminRequest request)
    {
        var validationResult = await createSubAdminValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await subAdminService.CreateAsync(request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool includeHidden = false)
    {
        var result = await subAdminService.GetAllAsync(includeHidden);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await subAdminService.GetByIdAsync(id);
        return Ok(result);
    }

    // The one endpoint on this controller a SubAdmin session calls about
    // itself - e.g. to know its own granted CanActivate* permissions before
    // offering a subscription-duration picker.
    [Authorize(Roles = "SubAdmin")]
    [HttpGet("me")]
    public async Task<IActionResult> GetMyInfo()
    {
        var result = await subAdminService.GetMyInfoAsync(User.GetSubAdminIdOrNull()!.Value);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id:guid}/logout")]
    public async Task<IActionResult> Logout(Guid id)
    {
        await subAdminService.LogoutAsync(id);
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        await subAdminService.DeactivateAsync(id);
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id:guid}/delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await subAdminService.DeleteAsync(id);
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id)
    {
        var result = await subAdminService.ReactivateAsync(id);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id:guid}/regenerate-qr")]
    public async Task<IActionResult> RegenerateQr(Guid id)
    {
        var result = await subAdminService.RegenerateQrAsync(id);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:guid}/permissions")]
    public async Task<IActionResult> SetPermissions(Guid id, UpdateSubAdminPermissionsRequest request)
    {
        var result = await subAdminService.SetPermissionsAsync(id, request);
        return Ok(result);
    }

    // Scanning a subscriber's own "account activation confirmation" QR (see
    // the tenant Settings page) attributes them for filtering/reporting -
    // never an access-control gate, every Admin/SubAdmin can always manage
    // every subscriber regardless (see UsersController). A SubAdmin caller
    // claims the subscriber for itself; the top Admin clears any existing
    // attribution - see UserService.ClaimSubscriberAsync for why this needs
    // no branching here.
    [Authorize(Roles = "Admin,SubAdmin")]
    [HttpPost("claim-subscriber")]
    public async Task<IActionResult> ClaimSubscriber(ClaimSubscriberRequest request)
    {
        await userService.ClaimSubscriberAsync(request.SubscriberId, User.GetSubAdminIdOrNull());
        return NoContent();
    }

    // That day's located actions for this SubAdmin, in chronological order -
    // the numbered, connected route the Admin sees on the map. start/end
    // are explicit UTC instants for the caller's local calendar day
    // (computed client-side), same as RepsController.GetRoute. Admin-only -
    // this is an oversight tool the SubAdmin itself has no use for.
    [Authorize(Roles = "Admin")]
    [HttpGet("{id:guid}/route")]
    public async Task<IActionResult> GetRoute(Guid id, [FromQuery] DateTime start, [FromQuery] DateTime end)
    {
        var result = await subAdminActivityService.GetRouteAsync(id, start, end);
        return Ok(result);
    }

    // Every SubAdmin-performed activation/suspend/reactivate from the last
    // rolling 24 hours across the whole platform, newest first - the
    // Admin's quick "what have my SubAdmins been doing" glance. Mirrors
    // RepsController.GetActivityFeed.
    [Authorize(Roles = "Admin")]
    [HttpGet("activity-feed")]
    public async Task<IActionResult> GetActivityFeed()
    {
        var result = await subAdminActivityService.GetActivityFeedAsync();
        return Ok(result);
    }

    // No username/password to authenticate with at this point - this is the
    // one action on this controller anyone can call before being signed in
    // at all. Mirrors RepsController.LoginByQr.
    [AllowAnonymous]
    [HttpPost("login-by-qr")]
    public async Task<IActionResult> LoginByQr(SubAdminLoginRequest request)
    {
        var result = await subAdminAuthService.LoginByQrAsync(request.QrToken);
        return Ok(result);
    }
}
