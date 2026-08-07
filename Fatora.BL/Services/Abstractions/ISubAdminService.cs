using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

namespace Fatora.BL.Services.Abstractions;

public interface ISubAdminService
{
    Task<SubAdminResponse> CreateAsync(CreateSubAdminRequest request);
    Task<List<SubAdminResponse>> GetAllAsync(bool includeHidden = false);
    Task<SubAdminResponse> GetByIdAsync(Guid subAdminId);
    Task<SubAdminResponse> GetMyInfoAsync(Guid subAdminId);
    Task LogoutAsync(Guid subAdminId);
    Task DeactivateAsync(Guid subAdminId);
    Task DeleteAsync(Guid subAdminId);
    Task<SubAdminResponse> ReactivateAsync(Guid subAdminId);
    Task<SubAdminResponse> RegenerateQrAsync(Guid subAdminId);
    Task<SubAdminResponse> SetPermissionsAsync(Guid subAdminId, UpdateSubAdminPermissionsRequest request);
}
