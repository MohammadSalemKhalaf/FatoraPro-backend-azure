namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

public interface ICustomerService
{
    Task<CustomerResponse> CreateAsync(Guid userId, CreateCustomerRequest request, Guid? createdByRepId = null);
    Task<List<CustomerResponse>> GetAllAsync(Guid userId, Guid? scopeToRepId = null);
    Task<CustomerResponse> GetByIdAsync(Guid userId, Guid id, Guid? scopeToRepId = null);
    Task<CustomerResponse> UpdateAsync(Guid userId, Guid id, UpdateCustomerRequest request, Guid? scopeToRepId = null);
    Task DeleteAsync(Guid userId, Guid id, Guid? scopeToRepId = null);
    Task<List<CustomerResponse>> GetArchivedAsync(Guid userId, Guid? scopeToRepId = null);
    Task<CustomerResponse> RestoreAsync(Guid userId, Guid id, Guid? scopeToRepId = null);
    Task PermanentDeleteAsync(Guid userId, Guid id, Guid? scopeToRepId = null);
}
