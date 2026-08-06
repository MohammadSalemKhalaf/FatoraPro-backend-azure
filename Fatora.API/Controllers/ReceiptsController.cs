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
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await receiptService.GetAllAsync(User.GetEffectiveOwnerId(), User.GetRepIdOrNull());
        return Ok(result);
    }
}
