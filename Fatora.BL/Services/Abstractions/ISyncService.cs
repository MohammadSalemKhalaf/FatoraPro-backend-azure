namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

public interface ISyncService
{
    Task<SyncPushResponse> PushAsync(Guid userId, SyncPushRequest request);
    Task<SyncPullResponse> PullAsync(Guid userId, DateTime since);
}
