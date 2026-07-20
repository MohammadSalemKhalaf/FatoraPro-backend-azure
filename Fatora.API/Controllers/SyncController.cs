using Fatora.API.Extensions;
using Fatora.API.Validators.SyncValidators;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "SalesRep")]
public class SyncController(ISyncService syncService, SyncPushRequestValidator pushValidator) : ControllerBase
{
    [HttpPost("push")]
    public async Task<IActionResult> Push(SyncPushRequest request)
    {
        var validationResult = await pushValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await syncService.PushAsync(User.GetUserId(), request);
        return Ok(result);
    }

    [HttpGet("pull")]
    public async Task<IActionResult> Pull([FromQuery] DateTime since)
    {
        var result = await syncService.PullAsync(User.GetUserId(), since);
        return Ok(result);
    }
}
