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
[Authorize(Roles = "SalesRep")]
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

        var result = await customerService.CreateAsync(User.GetUserId(), request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await customerService.GetAllAsync(User.GetUserId());
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await customerService.GetByIdAsync(User.GetUserId(), id);
        return Ok(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateCustomerRequest request)
    {
        var validationResult = await updateValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await customerService.UpdateAsync(User.GetUserId(), id, request);
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await customerService.DeleteAsync(User.GetUserId(), id);
        return NoContent();
    }

    [HttpGet("archived")]
    public async Task<IActionResult> GetArchived()
    {
        var result = await customerService.GetArchivedAsync(User.GetUserId());
        return Ok(result);
    }

    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id)
    {
        var result = await customerService.RestoreAsync(User.GetUserId(), id);
        return Ok(result);
    }

    [HttpDelete("{id:int}/permanent")]
    public async Task<IActionResult> PermanentDelete(int id)
    {
        await customerService.PermanentDeleteAsync(User.GetUserId(), id);
        return NoContent();
    }
}
