namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Responses;

public interface IRepActivityService
{
    // Only Orders/Receipts that actually have a location are returned as
    // plottable points - see RepRouteResponse.UnlocatedCount for how many
    // were left out. [startUtc, endUtc) is an explicit UTC instant range,
    // not a bare calendar date - CreatedAt is stored in true UTC, but the
    // caller's "day" is a local calendar day, so the client (which knows
    // its own timezone) computes the correct UTC window and this service
    // just filters on it verbatim, rather than guessing a timezone here.
    Task<RepRouteResponse> GetRouteAsync(
        Guid ownerUserId,
        Guid repId,
        DateTime startUtc,
        DateTime endUtc
    );

    // Every rep-created Order/Receipt across all of this owner's reps from
    // the last 24 hours, newest first - a rolling window, not a fixed day:
    // an item simply stops appearing once it's more than 24 hours old, no
    // matter when this is called.
    Task<List<RepActivityItemResponse>> GetActivityFeedAsync(Guid ownerUserId);
}
