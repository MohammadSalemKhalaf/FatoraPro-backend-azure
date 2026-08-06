using Fatora.API.Extensions;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "SalesRep,Rep")]
public class ReceiptsController(IReceiptService receiptService) : ControllerBase
{
    // repId lets the owner narrow the list to one rep's own receipts - a
    // Rep session's own calls stay scoped to its own id regardless (see
    // OrdersController.GetAll for the identical convention).
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? repId)
    {
        var result = await receiptService.GetAllAsync(User.GetEffectiveOwnerId(), User.GetRepIdOrNull() ?? repId);
        return Ok(result);
    }
}
