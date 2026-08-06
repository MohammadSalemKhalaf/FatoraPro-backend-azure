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
[Authorize(Roles = "SalesRep,Rep")]
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

        var result = await orderService.CreateAsync(User.GetEffectiveOwnerId(), request, User.GetRepIdOrNull());
        return StatusCode(StatusCodes.Status201Created, result);
    }

    // skip/take are optional and additive: omitting both preserves the
    // original "return everything" behavior existing callers (customer
    // invoice history, data export, barcode lookups) still rely on. Only the
    // paginated invoice list passes them. repId lets the owner narrow the
    // list to one rep's own invoices - a Rep session ignores it and is
    // always scoped to its own id regardless (see scopeToRepId below).
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? skip, [FromQuery] int? take, [FromQuery] Guid? repId)
    {
        var scopeToRepId = User.GetRepIdOrNull() ?? repId;

        if (skip is null && take is null)
        {
            var all = await orderService.GetAllAsync(User.GetEffectiveOwnerId(), scopeToRepId);
            return Ok(all);
        }

        var result = await orderService.GetPagedAsync(
            User.GetEffectiveOwnerId(), skip ?? 0, Math.Clamp(take ?? 20, 1, 100), scopeToRepId);
        return Ok(result);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] SummaryPeriod period = SummaryPeriod.All)
    {
        var result = await orderService.GetSummaryAsync(User.GetEffectiveOwnerId(), period);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await orderService.GetByIdAsync(User.GetEffectiveOwnerId(), id, User.GetRepIdOrNull());
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

        var result = await orderService.UpdateAsync(User.GetEffectiveOwnerId(), id, request, User.GetRepIdOrNull());
        return Ok(result);
    }

    // Owner-only: a Rep session can never hard-delete an invoice, full stop
    // - it only ever has "مرتجع" (Return) below. Enforced here, not just
    // hidden in the UI.
    [Authorize(Roles = "SalesRep")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await orderService.DeleteAsync(User.GetEffectiveOwnerId(), id, User.GetRepIdOrNull());
        return NoContent();
    }

    // Both the owner and a Rep can mark an invoice as returned - unlike
    // Delete, this keeps the invoice (and its audit trail) fully visible.
    [HttpPost("{id:guid}/return")]
    public async Task<IActionResult> Return(Guid id)
    {
        var result = await orderService.ReturnAsync(User.GetEffectiveOwnerId(), id, User.GetRepIdOrNull());
        return Ok(result);
    }

    [HttpPost("{id:guid}/record-payment")]
    public async Task<IActionResult> RecordPayment(Guid id, RecordPaymentRequest request)
    {
        var validationResult = await recordPaymentValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await orderService.RecordPaymentAsync(User.GetEffectiveOwnerId(), id, request, User.GetRepIdOrNull());
        return Ok(result);
    }
}
