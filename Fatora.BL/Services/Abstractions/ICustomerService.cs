namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

public interface ICustomerService
{
    Task<CustomerResponse> CreateAsync(Guid userId, CreateCustomerRequest request);
    Task<List<CustomerResponse>> GetAllAsync(Guid userId);
    Task<CustomerResponse?> GetByIdAsync(Guid userId, int id);
    Task<CustomerResponse?> UpdateAsync(Guid userId, int id, UpdateCustomerRequest request);
    Task<bool> DeleteAsync(Guid userId, int id);
    Task<List<CustomerResponse>> GetArchivedAsync(Guid userId);
    Task<CustomerResponse?> RestoreAsync(Guid userId, int id);
    Task<bool> PermanentDeleteAsync(Guid userId, int id);
}
