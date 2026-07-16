using Fatora.API.Extensions;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "SalesRep")]
public class OrdersController(IOrderService orderService, IPaymentService paymentService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateOrderRequest request)
    {
        var result = await orderService.CreateAsync(User.GetUserId(), request);

        if (result is null)
        {
            return BadRequest(new { message = "Customer or one or more products were not found." });
        }

        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await orderService.GetAllAsync(User.GetUserId());
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
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateOrderRequest request)
    {
        var result = await orderService.UpdateAsync(User.GetUserId(), id, request);

        if (result is null)
        {
            return NotFound(new { message = "Order not found, or customer/one or more products were not found." });
        }

        return Ok(result);
    }

    [HttpPost("{orderId:guid}/payments")]
    public async Task<IActionResult> AddPayment(Guid orderId, CreatePaymentRequest request)
    {
        try
        {
            var result = await paymentService.AddPaymentAsync(User.GetUserId(), orderId, request);
            return result is null ? NotFound() : StatusCode(StatusCodes.Status201Created, result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{orderId:guid}/payments")]
    public async Task<IActionResult> GetPayments(Guid orderId)
    {
        var result = await paymentService.GetPaymentsAsync(User.GetUserId(), orderId);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{orderId:guid}/payments/{paymentId:int}")]
    public async Task<IActionResult> DeletePayment(Guid orderId, int paymentId)
    {
        var deleted = await paymentService.DeletePaymentAsync(User.GetUserId(), orderId, paymentId);
        return deleted ? NoContent() : NotFound();
    }
}
