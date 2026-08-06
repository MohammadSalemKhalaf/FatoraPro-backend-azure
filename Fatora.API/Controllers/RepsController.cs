using Fatora.API.Extensions;
using Fatora.API.Validators.RepValidators;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

[Route("api/reps")]
[ApiController]
[Authorize(Roles = "SalesRep")]
public class RepsController(
    IRepService repService,
    IRepAuthService repAuthService,
    CreateRepRequestValidator createRepValidator) : ControllerBase
{
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

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await repService.GetAllAsync(User.GetUserId());
        return Ok(result);
    }

    [HttpPost("{id:guid}/logout")]
    public async Task<IActionResult> Logout(Guid id)
    {
        await repService.LogoutAsync(User.GetUserId(), id);
        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        await repService.DeactivateAsync(User.GetUserId(), id);
        return NoContent();
    }

    [HttpPost("{id:guid}/regenerate-qr")]
    public async Task<IActionResult> RegenerateQr(Guid id)
    {
        var result = await repService.RegenerateQrAsync(User.GetUserId(), id);
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
