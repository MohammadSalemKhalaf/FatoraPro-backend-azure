using Fatora.API.Validators.SupportContactValidators;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

// Platform-wide config, not SubAdmin-delegable (unlike subscription
// activation) - every management action is Admin-only. The one exception,
// GetPublic, is deliberately [AllowAnonymous]: it's what a not-yet-signed-in
// login page and every tenant's own Settings "الدعم الفني" section both
// need, and only ever returns IsActive rows (see SupportContactService).
[Route("api/support-contacts")]
[ApiController]
public class SupportContactsController(
    ISupportContactService supportContactService,
    CreateSupportContactRequestValidator createValidator,
    UpdateSupportContactRequestValidator updateValidator) : ControllerBase
{
    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await supportContactService.GetAllAsync(activeOnly: false);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("public")]
    public async Task<IActionResult> GetPublic()
    {
        var result = await supportContactService.GetAllAsync(activeOnly: true);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateSupportContactRequest request)
    {
        var validationResult = await createValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await supportContactService.CreateAsync(request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateSupportContactRequest request)
    {
        var validationResult = await updateValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await supportContactService.UpdateAsync(id, request);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await supportContactService.DeleteAsync(id);
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("reorder")]
    public async Task<IActionResult> Reorder(ReorderSupportContactsRequest request)
    {
        await supportContactService.ReorderAsync(request);
        return NoContent();
    }
}
