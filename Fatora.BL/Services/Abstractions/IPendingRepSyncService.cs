namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

public interface IPendingRepSyncService
{
    // Called only through the narrow RepPendingSync token - repId comes
    // straight from that token's own identity, never a caller-supplied
    // value, so there's no ownership check to make here.
    Task UploadAsync(Guid repId, SyncPushRequest request);

    Task<PendingRepSyncSummaryResponse> GetSummaryAsync(Guid ownerUserId, Guid repId);

    // Replays the queued batch through the exact same SyncService.PushAsync
    // a live Rep session's own sync already goes through, then clears the
    // batch. Returns the per-item Applied/Conflict/Rejected outcome so the
    // owner isn't just told "done" with no visibility into partial results.
    Task<SyncPushResponse> ApplyAsync(Guid ownerUserId, Guid repId);

    Task DiscardAsync(Guid ownerUserId, Guid repId);
}
