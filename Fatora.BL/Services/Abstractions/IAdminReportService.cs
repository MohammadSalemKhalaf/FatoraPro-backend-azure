using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

namespace Fatora.BL.Services.Abstractions;

public interface IAdminReportService
{
    // actingSubAdminId: null (top Admin) returns the full platform
    // dashboard plus every active SubAdmin's breakdown; non-null (a
    // SubAdmin caller) returns only that SubAdmin's own slice, with
    // Platform left null - see AdminDashboardResponse.
    public Task<AdminDashboardResponse> GetDashboardAsync(AdminReportPeriod period, Guid? actingSubAdminId);
}
