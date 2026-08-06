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

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id)
    {
        var result = await customerService.RestoreAsync(User.GetEffectiveOwnerId(), id, User.GetRepIdOrNull());
        return Ok(result);
    }

    [HttpDelete("{id:guid}/permanent")]
    public async Task<IActionResult> PermanentDelete(Guid id)
    {
        await customerService.PermanentDeleteAsync(User.GetEffectiveOwnerId(), id, User.GetRepIdOrNull());
        return NoContent();
    }
}
