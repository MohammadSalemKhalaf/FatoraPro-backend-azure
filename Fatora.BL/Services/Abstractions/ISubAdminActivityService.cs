using Fatora.BL.DTOs.Responses;

namespace Fatora.BL.Services.Abstractions;

public interface ISubAdminActivityService
{
    // That day's located actions for one specific SubAdmin, in
    // chronological order - mirrors IRepActivityService.GetRouteAsync.
    public Task<SubAdminRouteResponse> GetRouteAsync(Guid subAdminId, DateTime startUtc, DateTime endUtc);

    // Every SubAdmin-performed activation/suspend/reactivate from the last
    // rolling 24 hours across the whole platform, newest first - the
    // Admin's quick "what have my SubAdmins been doing" glance. Mirrors
    // IRepActivityService.GetActivityFeedAsync.
    public Task<List<SubAdminActivityItemResponse>> GetActivityFeedAsync();
}
