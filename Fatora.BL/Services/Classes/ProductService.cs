using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;

namespace Fatora.BL.Services.Classes;

public class ProductService(AppDbContext dbContext) : IProductService
{
    public async Task<ProductResponse> CreateAsync(Guid userId, CreateProductRequest request)
    {
        var product = new Product
        {
            Name = request.Name,
            PurchasePrice = request.PurchasePrice,
            SellPrice = request.SellPrice,
            UserId = userId
        };

        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync();

        return ToResponse(product);
    }

    public async Task<List<ProductResponse>> GetAllAsync(Guid userId)
    {
        var products = await dbContext.Products
            .Where(p => p.UserId == userId && p.IsActive)
            .ToListAsync();

        return products.Select(ToResponse).ToList();
    }

    public async Task<ProductResponse?> GetByIdAsync(Guid userId, int id)
    {
        var product = await FindOwnedActiveProduct(userId, id);
        return product is null ? null : ToResponse(product);
    }

    public async Task<ProductResponse?> UpdateAsync(Guid userId, int id, UpdateProductRequest request)
    {
        var product = await FindOwnedActiveProduct(userId, id);

        if (product is null)
        {
            return null;
        }

        product.Name = request.Name;
        product.PurchasePrice = request.PurchasePrice;
        product.SellPrice = request.SellPrice;

        await dbContext.SaveChangesAsync();

        return ToResponse(product);
    }

    public async Task<bool> DeleteAsync(Guid userId, int id)
    {
        var product = await FindOwnedActiveProduct(userId, id);

        if (product is null)
        {
            return false;
        }

        product.IsActive = false;
        await dbContext.SaveChangesAsync();

        return true;
    }

    public async Task<List<ProductResponse>> GetArchivedAsync(Guid userId)
    {
        var products = await dbContext.Products
            .Where(p => p.UserId == userId && !p.IsActive)
            .ToListAsync();

        return products.Select(ToResponse).ToList();
    }

    public async Task<ProductResponse?> RestoreAsync(Guid userId, int id)
    {
        var product = await FindOwnedProduct(userId, id);

        if (product is null)
        {
            return null;
        }

        product.IsActive = true;
        await dbContext.SaveChangesAsync();

        return ToResponse(product);
    }

    public async Task<bool> PermanentDeleteAsync(Guid userId, int id)
    {
        var product = await FindOwnedProduct(userId, id);

        if (product is null)
        {
            return false;
        }

        dbContext.Products.Remove(product);
        await dbContext.SaveChangesAsync();

        return true;
    }

    private async Task<Product?> FindOwnedActiveProduct(Guid userId, int id) =>
        await dbContext.Products.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId && p.IsActive);

    private async Task<Product?> FindOwnedProduct(Guid userId, int id) =>
        await dbContext.Products.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

    private static ProductResponse ToResponse(Product product) => new()
    {
        Id = product.Id,
        Name = product.Name,
        PurchasePrice = product.PurchasePrice,
        SellPrice = product.SellPrice,
        IsActive = product.IsActive
    };
}
