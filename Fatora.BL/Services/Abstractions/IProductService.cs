namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

public interface IProductService
{
    Task<ProductResponse> CreateAsync(Guid userId, CreateProductRequest request, Guid? createdByRepId = null);
    Task<List<ProductResponse>> GetAllAsync(Guid userId, Guid? scopeToRepId = null);
    Task<List<ProductResponse>> GetPagedAsync(Guid userId, int skip, int take, Guid? scopeToRepId = null);
    Task<ProductResponse> GetByIdAsync(Guid userId, Guid id, Guid? scopeToRepId = null);
    Task<ProductResponse> UpdateAsync(Guid userId, Guid id, UpdateProductRequest request, Guid? scopeToRepId = null);
    Task<ProductResponse> UpdateImageAsync(Guid userId, Guid id, string imageUrl);
    Task<ProductResponse> DeleteImageAsync(Guid userId, Guid id);
    Task DeleteAsync(Guid userId, Guid id, Guid? scopeToRepId = null);
    Task<List<ProductResponse>> GetArchivedAsync(Guid userId, Guid? scopeToRepId = null);
    Task<ProductResponse> RestoreAsync(Guid userId, Guid id);
    Task PermanentDeleteAsync(Guid userId, Guid id);
}
