namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

public interface IProductService
{
    Task<ProductResponse> CreateAsync(Guid userId, CreateProductRequest request);
    Task<List<ProductResponse>> GetAllAsync(Guid userId);
    Task<ProductResponse> GetByIdAsync(Guid userId, int id);
    Task<ProductResponse> UpdateAsync(Guid userId, int id, UpdateProductRequest request);
    Task<ProductResponse> UpdateImageAsync(Guid userId, int id, string imageUrl);
    Task DeleteAsync(Guid userId, int id);
    Task<List<ProductResponse>> GetArchivedAsync(Guid userId);
    Task<ProductResponse> RestoreAsync(Guid userId, int id);
    Task PermanentDeleteAsync(Guid userId, int id);
}
