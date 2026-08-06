using Fatora.API.Extensions;
using Fatora.API.Validators.SyncValidators;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

// Separate from RepsController/SyncController deliberately: this is the one
// endpoint a RepPendingSync token is authorized for (see
// AccountStatusFilter/RepAuthService.GeneratePendingSyncToken), and mixing
// that role into either of those controllers' own class-level [Authorize]
// would AND-combine the role requirements into something no real token
// could ever satisfy (see RepsController's own comment on this exact
// pitfall).
[Route("api/pending-sync")]
[ApiController]
[Authorize(Roles = "RepPendingSync")]
public class PendingSyncController(
    IPendingRepSyncService pendingRepSyncService,
    SyncPushRequestValidator pushValidator) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Upload(SyncPushRequest request)
    {
        var validationResult = await pushValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        await pendingRepSyncService.UploadAsync(User.GetUserId(), request);
        return NoContent();
    }
}
