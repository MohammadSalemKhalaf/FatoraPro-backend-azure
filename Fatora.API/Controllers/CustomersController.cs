using Fatora.API.Extensions;
using Fatora.API.Validators.CustomerValidators;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "SalesRep,Rep")]
public class CustomersController(
    ICustomerService customerService,
    CreateCustomerRequestValidator createValidator,
    UpdateCustomerRequestValidator updateValidator) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateCustomerRequest request)
    {
        var validationResult = await createValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await customerService.CreateAsync(User.GetEffectiveOwnerId(), request, User.GetRepIdOrNull());
        return StatusCode(StatusCodes.Status201Created, result);
    }

    // repId lets the owner narrow the list to one rep's own customers - a
    // Rep session ignores it and is always scoped to its own id regardless.
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? repId)
    {
        var result = await customerService.GetAllAsync(User.GetEffectiveOwnerId(), User.GetRepIdOrNull() ?? repId);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await customerService.GetByIdAsync(User.GetEffectiveOwnerId(), id, User.GetRepIdOrNull());
        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateCustomerRequest request)
    {
        var validationResult = await updateValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await customerService.UpdateAsync(User.GetEffectiveOwnerId(), id, request, User.GetRepIdOrNull());
        return Ok(result);
    }

    // Owner-only - unlike Update, this is never "own creation only" for a
    // Rep, it's blocked outright even on a customer the Rep created itself.
    // A customer's statement/debt view degrades the moment it's archived
    // (see CustomerService.PermanentDeleteAsync's related cascade concern
    // for the equally destructive hard-delete case), and that's a call only
    // the owner gets to make - not something a sub-account discovers by
    // tidying up its own contact list. Enforced here, not just hidden in the
    // UI. Mirrored in SyncService.PushCustomerAsync, the path the app
    // actually uses for this in normal (offline-first) operation.
    [Authorize(Roles = "SalesRep")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await customerService.DeleteAsync(User.GetEffectiveOwnerId(), id, User.GetRepIdOrNull());
        return NoContent();
    }

    [HttpGet("archived")]
    public async Task<IActionResult> GetArchived()
    {
        var result = await customerService.GetArchivedAsync(User.GetEffectiveOwnerId(), User.GetRepIdOrNull());
        return Ok(result);
    }

    [Authorize(Roles = "SalesRep")]
    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id)
    {
        var result = await customerService.RestoreAsync(User.GetEffectiveOwnerId(), id, User.GetRepIdOrNull());
        return Ok(result);
    }

    // Owner-only, matching ProductsController's identical pair and
    // OrdersController.Delete: this is a real hard delete, and Customer is the
    // FK parent of Orders and Receipts with cascade behaviour, so a Rep
    // reaching it would take that customer's whole invoice and payment history
    // with it - including rows the owner or another rep created. Enforced
    // here, not just hidden in the UI (the Flutter client only ever
    // soft-deletes).
    [Authorize(Roles = "SalesRep")]
    [HttpDelete("{id:guid}/permanent")]
    public async Task<IActionResult> PermanentDelete(Guid id)
    {
        await customerService.PermanentDeleteAsync(User.GetEffectiveOwnerId(), id, User.GetRepIdOrNull());
        return NoContent();
    }
}
