namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

public interface ICustomerService
{
    Task<CustomerResponse> CreateAsync(Guid userId, CreateCustomerRequest request);
    Task<List<CustomerResponse>> GetAllAsync(Guid userId);
    Task<CustomerResponse> GetByIdAsync(Guid userId, Guid id);
    Task<CustomerResponse> UpdateAsync(Guid userId, Guid id, UpdateCustomerRequest request);
    Task DeleteAsync(Guid userId, Guid id);
    Task<List<CustomerResponse>> GetArchivedAsync(Guid userId);
    Task<CustomerResponse> RestoreAsync(Guid userId, Guid id);
    Task PermanentDeleteAsync(Guid userId, Guid id);
}
