using Fatora.API.Extensions;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "SalesRep,Rep")]
public class ReportsController(IReportService reportService) : ControllerBase
{
    // repId lets the owner narrow every report to one rep's own orders - a
    // Rep session's own calls stay scoped to its own id regardless (see
    // OrdersController.GetAll for the identical convention).
    [HttpGet("sales")]
    public async Task<IActionResult> GetSales([FromQuery] SummaryPeriod period = SummaryPeriod.All, [FromQuery] Guid? repId = null)
    {
        var result = await reportService.GetSalesAsync(User.GetEffectiveOwnerId(), period, User.GetRepIdOrNull() ?? repId);
        return Ok(result);
    }

    [HttpGet("items")]
    public async Task<IActionResult> GetItems([FromQuery] SummaryPeriod period = SummaryPeriod.All, [FromQuery] ItemsSortBy sortBy = ItemsSortBy.MostSold, [FromQuery] Guid? repId = null)
    {
        var result = await reportService.GetItemsAsync(User.GetEffectiveOwnerId(), period, sortBy, User.GetRepIdOrNull() ?? repId);
        return Ok(result);
    }

    [HttpGet("clients")]
    public async Task<IActionResult> GetClients([FromQuery] SummaryPeriod period = SummaryPeriod.All, [FromQuery] ClientsSortBy sortBy = ClientsSortBy.MostInvoices, [FromQuery] Guid? repId = null)
    {
        var result = await reportService.GetClientsAsync(User.GetEffectiveOwnerId(), period, sortBy, User.GetRepIdOrNull() ?? repId);
        return Ok(result);
    }
}
