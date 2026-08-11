using Fatora.API.Extensions;
using Fatora.API.Validators.SyncValidators;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "SalesRep,Rep")]
public class SyncController(
    ISyncService syncService,
    SyncPushRequestValidator pushValidator,
    CustomerSyncItemValidator customerValidator,
    ProductSyncItemValidator productValidator,
    OrderSyncItemValidator orderValidator,
    ReceiptSyncItemValidator receiptValidator) : ControllerBase
{
    [HttpPost("push")]
    public async Task<IActionResult> Push(SyncPushRequest request)
    {
        // Only the whole-batch size guard rejects the entire request - every
        // other rule is checked per item below, so one malformed row (a
        // stray free-priced line, a zero-amount receipt, ...) can never
        // block every other row bundled in the same offline batch, which
        // for a rep syncing after hours offline can be everything they did
        // all day.
        var validationResult = await pushValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var (validCustomers, rejectedCustomers) =
            await PartitionAsync(request.Customers, customerValidator, c => c.Id);
        var (validProducts, rejectedProducts) =
            await PartitionAsync(request.Products, productValidator, p => p.Id);
        var (validOrders, rejectedOrders) =
            await PartitionAsync(request.Orders, orderValidator, o => o.Id);
        var (validReceipts, rejectedReceipts) =
            await PartitionAsync(request.Receipts, receiptValidator, r => r.Id);

        var validRequest = request with
        {
            Customers = validCustomers,
            Products = validProducts,
            Orders = validOrders,
            Receipts = validReceipts,
        };

        var result = await syncService.PushAsync(User.GetEffectiveOwnerId(), validRequest, User.GetRepIdOrNull());
        result.Customers.AddRange(rejectedCustomers);
        result.Products.AddRange(rejectedProducts);
        result.Orders.AddRange(rejectedOrders);
        result.Receipts.AddRange(rejectedReceipts);
        return Ok(result);
    }

    private static async Task<(List<T> valid, List<SyncItemResult> rejected)> PartitionAsync<T>(
        List<T> items,
        FluentValidation.IValidator<T> validator,
        Func<T, Guid> idOf)
    {
        var valid = new List<T>();
        var rejected = new List<SyncItemResult>();
        foreach (var item in items)
        {
            var itemResult = await validator.ValidateAsync(item);
            if (itemResult.IsValid)
            {
                valid.Add(item);
            }
            else
            {
                var message = string.Join("; ", itemResult.Errors.Select(e => e.ErrorMessage));
                rejected.Add(new SyncItemResult(idOf(item), "Rejected", message));
            }
        }
        return (valid, rejected);
    }

    [HttpGet("pull")]
    public async Task<IActionResult> Pull([FromQuery] DateTime since)
    {
        var result = await syncService.PullAsync(User.GetEffectiveOwnerId(), since, User.GetRepIdOrNull());
        return Ok(result);
    }
}
