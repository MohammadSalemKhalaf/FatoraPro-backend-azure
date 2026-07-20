namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

public interface IProductService
{
    Task<ProductResponse> CreateAsync(Guid userId, CreateProductRequest request);
    Task<List<ProductResponse>> GetAllAsync(Guid userId);
    Task<ProductResponse> GetByIdAsync(Guid userId, Guid id);
    Task<ProductResponse> UpdateAsync(Guid userId, Guid id, UpdateProductRequest request);
    Task<ProductResponse> UpdateImageAsync(Guid userId, Guid id, string imageUrl);
    Task DeleteAsync(Guid userId, Guid id);
    Task<List<ProductResponse>> GetArchivedAsync(Guid userId);
    Task<ProductResponse> RestoreAsync(Guid userId, Guid id);
    Task PermanentDeleteAsync(Guid userId, Guid id);
}
