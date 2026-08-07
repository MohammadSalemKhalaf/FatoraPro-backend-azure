using Fatora.API.Extensions;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

// A SubAdmin caller is deliberately allowed here (unlike most Admin-only
// controllers) - AdminReportService.GetDashboardAsync gates the response
// content itself, forcing it down to that SubAdmin's own slice server-side
// (Platform left null, SubAdminBreakdown limited to its own row) regardless
// of anything the caller sends, so this never needs a separate SubAdmin-only
// endpoint or client-side trust.
[Route("api/admin/reports")]
[ApiController]
[Authorize(Roles = "Admin,SubAdmin")]
public class AdminReportsController(IAdminReportService adminReportService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetDashboard([FromQuery] AdminReportPeriod period = AdminReportPeriod.Month)
    {
        var result = await adminReportService.GetDashboardAsync(period, User.GetSubAdminIdOrNull());
        return Ok(result);
    }
}
