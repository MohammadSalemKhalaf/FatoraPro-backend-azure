namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Responses;

public interface IRepActivityService
{
    // Only Orders/Receipts that actually have a location are returned as
    // plottable points - see RepRouteResponse.UnlocatedCount for how many
    // were left out.
    Task<RepRouteResponse> GetRouteAsync(Guid ownerUserId, Guid repId, DateOnly date);
}
