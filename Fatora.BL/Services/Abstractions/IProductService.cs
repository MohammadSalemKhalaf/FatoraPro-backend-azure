namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

public interface IProductService
{
    Task<ProductResponse> CreateAsync(Guid userId, CreateProductRequest request);
    Task<List<ProductResponse>> GetAllAsync(Guid userId);
    Task<ProductResponse?> GetByIdAsync(Guid userId, int id);
    Task<ProductResponse?> UpdateAsync(Guid userId, int id, UpdateProductRequest request);
    Task<bool> DeleteAsync(Guid userId, int id);
    Task<List<ProductResponse>> GetArchivedAsync(Guid userId);
    Task<ProductResponse?> RestoreAsync(Guid userId, int id);
    Task<bool> PermanentDeleteAsync(Guid userId, int id);
}
