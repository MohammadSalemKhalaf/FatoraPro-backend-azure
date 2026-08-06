namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

public interface IRepService
{
    Task<RepResponse> CreateAsync(Guid ownerUserId, CreateRepRequest request);
    Task<List<RepResponse>> GetAllAsync(Guid ownerUserId);
    Task<RepResponse> GetByIdAsync(Guid ownerUserId, Guid repId);
    Task<RepResponse> GetMyInfoAsync(Guid repId);
    Task LogoutAsync(Guid ownerUserId, Guid repId);
    Task DeactivateAsync(Guid ownerUserId, Guid repId);
    Task<RepResponse> ReactivateAsync(Guid ownerUserId, Guid repId);
    Task<RepResponse> RegenerateQrAsync(Guid ownerUserId, Guid repId);
    Task<RepResponse> SetProductAccessAsync(Guid ownerUserId, Guid repId, UpdateRepProductAccessRequest request);
    Task<RepResponse> SetCustomerAccessAsync(Guid ownerUserId, Guid repId, UpdateRepCustomerAccessRequest request);
    Task<RepAccessListResponse> GetProductAccessAsync(Guid ownerUserId, Guid repId);
    Task<RepAccessListResponse> GetCustomerAccessAsync(Guid ownerUserId, Guid repId);
}
