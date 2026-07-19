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
    IPaymentService paymentService,
    CreateOrderRequestValidator createOrderValidator,
    UpdateOrderRequestValidator updateOrderValidator,
    CreatePaymentRequestValidator createPaymentValidator) : ControllerBase
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

    [HttpPost("{orderId:guid}/payments")]
    public async Task<IActionResult> AddPayment(Guid orderId, CreatePaymentRequest request)
    {
        var validationResult = await createPaymentValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await paymentService.AddPaymentAsync(User.GetUserId(), orderId, request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("{orderId:guid}/payments")]
    public async Task<IActionResult> GetPayments(Guid orderId)
    {
        var result = await paymentService.GetPaymentsAsync(User.GetUserId(), orderId);
        return Ok(result);
    }

    [HttpDelete("{orderId:guid}/payments/{paymentId:int}")]
    public async Task<IActionResult> DeletePayment(Guid orderId, int paymentId)
    {
        await paymentService.DeletePaymentAsync(User.GetUserId(), orderId, paymentId);
        return NoContent();
    }
}
