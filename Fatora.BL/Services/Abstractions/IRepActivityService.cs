namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Responses;

public interface IRepActivityService
{
    // Only Orders/Receipts that actually have a location are returned as
    // plottable points - see RepRouteResponse.UnlocatedCount for how many
    // were left out.
    Task<RepRouteResponse> GetRouteAsync(Guid ownerUserId, Guid repId, DateOnly date);

    // Every rep-created Order/Receipt across all of this owner's reps from
    // the last 24 hours, newest first - a rolling window, not a fixed day:
    // an item simply stops appearing once it's more than 24 hours old, no
    // matter when this is called.
    Task<List<RepActivityItemResponse>> GetActivityFeedAsync(Guid ownerUserId);
}
