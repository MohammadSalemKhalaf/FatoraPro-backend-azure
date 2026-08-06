namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

public interface IRepService
{
    Task<RepResponse> CreateAsync(Guid ownerUserId, CreateRepRequest request);
    Task<List<RepResponse>> GetAllAsync(Guid ownerUserId);
    Task<RepResponse> GetByIdAsync(Guid ownerUserId, Guid repId);
    Task LogoutAsync(Guid ownerUserId, Guid repId);
    Task DeactivateAsync(Guid ownerUserId, Guid repId);
    Task<RepResponse> RegenerateQrAsync(Guid ownerUserId, Guid repId);
    Task<RepResponse> SetProductAccessAsync(Guid ownerUserId, Guid repId, UpdateRepProductAccessRequest request);
}
