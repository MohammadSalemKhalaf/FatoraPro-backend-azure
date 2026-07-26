using Fatora.API.Extensions;
using Fatora.API.Validators.OrderValidators;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "SalesRep")]
public class OrdersController(
    IOrderService orderService,
    CreateOrderRequestValidator createOrderValidator,
    UpdateOrderRequestValidator updateOrderValidator,
    RecordPaymentRequestValidator recordPaymentValidator) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateOrderRequest request)
    {
        var validationResult = await createOrderValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await orderService.CreateAsync(User.GetUserId(), request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    // skip/take are optional and additive: omitting both preserves the
    // original "return everything" behavior existing callers (customer
    // invoice history, data export, barcode lookups) still rely on. Only the
    // paginated invoice list passes them.
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? skip, [FromQuery] int? take)
    {
        if (skip is null && take is null)
        {
            var all = await orderService.GetAllAsync(User.GetUserId());
            return Ok(all);
        }

        var result = await orderService.GetPagedAsync(User.GetUserId(), skip ?? 0, Math.Clamp(take ?? 20, 1, 100));
        return Ok(result);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] SummaryPeriod period = SummaryPeriod.All)
    {
        var result = await orderService.GetSummaryAsync(User.GetUserId(), period);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await orderService.GetByIdAsync(User.GetUserId(), id);
        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateOrderRequest request)
    {
        var validationResult = await updateOrderValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await orderService.UpdateAsync(User.GetUserId(), id, request);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await orderService.DeleteAsync(User.GetUserId(), id);
        return NoContent();
    }

    [HttpPost("{id:guid}/record-payment")]
    public async Task<IActionResult> RecordPayment(Guid id, RecordPaymentRequest request)
    {
        var validationResult = await recordPaymentValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await orderService.RecordPaymentAsync(User.GetUserId(), id, request);
        return Ok(result);
    }
}
