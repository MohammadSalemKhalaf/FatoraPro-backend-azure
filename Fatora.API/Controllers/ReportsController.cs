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
    [HttpGet("sales")]
    public async Task<IActionResult> GetSales([FromQuery] SummaryPeriod period = SummaryPeriod.All)
    {
        var result = await reportService.GetSalesAsync(User.GetEffectiveOwnerId(), period);
        return Ok(result);
    }

    [HttpGet("items")]
    public async Task<IActionResult> GetItems([FromQuery] SummaryPeriod period = SummaryPeriod.All, [FromQuery] ItemsSortBy sortBy = ItemsSortBy.MostSold)
    {
        var result = await reportService.GetItemsAsync(User.GetEffectiveOwnerId(), period, sortBy);
        return Ok(result);
    }

    [HttpGet("clients")]
    public async Task<IActionResult> GetClients([FromQuery] SummaryPeriod period = SummaryPeriod.All, [FromQuery] ClientsSortBy sortBy = ClientsSortBy.MostInvoices)
    {
        var result = await reportService.GetClientsAsync(User.GetEffectiveOwnerId(), period, sortBy);
        return Ok(result);
    }
}
