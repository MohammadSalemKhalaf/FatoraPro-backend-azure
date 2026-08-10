using Fatora.API.Extensions;
using Fatora.API.Validators.RepValidators;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

// Class-level "SalesRep,Rep" (broadest) with every owner-only action below
// narrowing back down to "SalesRep" - multiple [Authorize] attributes AND
// their role sets together rather than the method-level one overriding the
// class-level one, so a narrow class-level restriction can never be
// "widened" by a broader method-level attribute (that combination is
// simply unsatisfiable). Same pattern ProductsController already uses.
[Route("api/reps")]
[ApiController]
[Authorize(Roles = "SalesRep,Rep")]
public class RepsController(
    IRepService repService,
    IRepAuthService repAuthService,
    IPendingRepSyncService pendingRepSyncService,
    IRepActivityService repActivityService,
    CreateRepRequestValidator createRepValidator) : ControllerBase
{
    [Authorize(Roles = "SalesRep")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateRepRequest request)
    {
        var validationResult = await createRepValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await repService.CreateAsync(User.GetUserId(), request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    // includeHidden=true is how RepFilterRow's "filter by rep" picker still
    // resolves a deleted rep - the plain reps-management list (RepsPage)
    // never passes it, which is what actually keeps that page decluttered.
    [Authorize(Roles = "SalesRep")]
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool includeHidden = false)
    {
        var result = await repService.GetAllAsync(User.GetUserId(), includeHidden);
        return Ok(result);
    }

    [Authorize(Roles = "SalesRep")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await repService.GetByIdAsync(User.GetUserId(), id);
        return Ok(result);
    }

    // The one endpoint on this controller a Rep session itself calls (about
    // its own account) rather than the owner - e.g. to know its current
    // ProductAccessMode before offering "create my own product".
    [Authorize(Roles = "Rep")]
    [HttpGet("me")]
    public async Task<IActionResult> GetMyInfo()
    {
        var result = await repService.GetMyInfoAsync(User.GetRepIdOrNull()!.Value);
        return Ok(result);
    }

    [Authorize(Roles = "SalesRep")]
    [HttpPost("{id:guid}/logout")]
    public async Task<IActionResult> Logout(Guid id)
    {
        await repService.LogoutAsync(User.GetUserId(), id);
        return NoContent();
    }

    [Authorize(Roles = "SalesRep")]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        await repService.DeactivateAsync(User.GetUserId(), id);
        return NoContent();
    }

    [Authorize(Roles = "SalesRep")]
    [HttpPost("{id:guid}/delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await repService.DeleteAsync(User.GetUserId(), id);
        return NoContent();
    }

    [Authorize(Roles = "SalesRep")]
    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id)
    {
        var result = await repService.ReactivateAsync(User.GetUserId(), id);
        return Ok(result);
    }

    [Authorize(Roles = "SalesRep")]
    [HttpPost("{id:guid}/regenerate-qr")]
    public async Task<IActionResult> RegenerateQr(Guid id)
    {
        var result = await repService.RegenerateQrAsync(User.GetUserId(), id);
        return Ok(result);
    }

    [Authorize(Roles = "SalesRep")]
    [HttpGet("{id:guid}/product-access")]
    public async Task<IActionResult> GetProductAccess(Guid id)
    {
        var result = await repService.GetProductAccessAsync(User.GetUserId(), id);
        return Ok(result);
    }

    [Authorize(Roles = "SalesRep")]
    [HttpPut("{id:guid}/product-access")]
    public async Task<IActionResult> SetProductAccess(Guid id, UpdateRepProductAccessRequest request)
    {
        var result = await repService.SetProductAccessAsync(User.GetUserId(), id, request);
        return Ok(result);
    }

    [Authorize(Roles = "SalesRep")]
    [HttpGet("{id:guid}/customer-access")]
    public async Task<IActionResult> GetCustomerAccess(Guid id)
    {
        var result = await repService.GetCustomerAccessAsync(User.GetUserId(), id);
        return Ok(result);
    }

    [Authorize(Roles = "SalesRep")]
    [HttpPut("{id:guid}/customer-access")]
    public async Task<IActionResult> SetCustomerAccess(Guid id, UpdateRepCustomerAccessRequest request)
    {
        var result = await repService.SetCustomerAccessAsync(User.GetUserId(), id, request);
        return Ok(result);
    }

    // Whether this rep had unsynced offline work queued through the
    // narrow post-deactivation upload path (see PendingSyncController) -
    // and if so, how much, so the owner can decide with real information
    // instead of a blind "sync or discard".
    [Authorize(Roles = "SalesRep")]
    [HttpGet("{id:guid}/pending-sync")]
    public async Task<IActionResult> GetPendingSync(Guid id)
    {
        var result = await pendingRepSyncService.GetSummaryAsync(User.GetUserId(), id);
        return Ok(result);
    }

    [Authorize(Roles = "SalesRep")]
    [HttpPost("{id:guid}/pending-sync/apply")]
    public async Task<IActionResult> ApplyPendingSync(Guid id)
    {
        var result = await pendingRepSyncService.ApplyAsync(User.GetUserId(), id);
        return Ok(result);
    }

    [Authorize(Roles = "SalesRep")]
    [HttpPost("{id:guid}/pending-sync/discard")]
    public async Task<IActionResult> DiscardPendingSync(Guid id)
    {
        await pendingRepSyncService.DiscardAsync(User.GetUserId(), id);
        return NoContent();
    }

    // That day's located Orders/Receipts for this rep, in chronological
    // order - the numbered, connected route the owner sees on the map.
    // start/end are explicit UTC instants for the caller's local calendar
    // day (computed client-side, see reps_repository.dart's getRoute) -
    // not a bare date, since this server can't know the caller's timezone
    // on its own.
    [Authorize(Roles = "SalesRep")]
    [HttpGet("{id:guid}/route")]
    public async Task<IActionResult> GetRoute(Guid id, [FromQuery] DateTime start, [FromQuery] DateTime end)
    {
        var result = await repActivityService.GetRouteAsync(User.GetUserId(), id, start, end);
        return Ok(result);
    }

    // Every rep-created Order/Receipt from the last rolling 24 hours,
    // newest first - the owner's quick "what's my sales team been doing"
    // glance.
    [Authorize(Roles = "SalesRep")]
    [HttpGet("activity-feed")]
    public async Task<IActionResult> GetActivityFeed()
    {
        var result = await repActivityService.GetActivityFeedAsync(User.GetUserId());
        return Ok(result);
    }

    // A rep has no username/password to authenticate with at this point -
    // this is the one action on this controller anyone can call before
    // being signed in at all.
    [AllowAnonymous]
    [HttpPost("login-by-qr")]
    public async Task<IActionResult> LoginByQr(RepLoginRequest request)
    {
        var result = await repAuthService.LoginByQrAsync(request.QrToken);
        return Ok(result);
    }
}
