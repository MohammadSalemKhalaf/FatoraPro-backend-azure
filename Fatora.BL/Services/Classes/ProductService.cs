using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;

namespace Fatora.BL.Services.Classes;

public class ProductService(AppDbContext dbContext) : IProductService
{
    public async Task<ProductResponse> CreateAsync(Guid userId, CreateProductRequest request)
    {
        await EnsureBarcodeAvailableAsync(userId, request.Barcode, excludingProductId: null);

        var product = new Product
        {
            Name = request.Name,
            Description = request.Description,
            ImageUrl = request.ImageUrl,
            PurchasePrice = request.PurchasePrice,
            SellPrice = request.SellPrice,
            Barcode = request.Barcode,
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
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        return products.Select(ToResponse).ToList();
    }

    // Newest-first, with a stable sort so Skip/Take means the same "page"
    // on every call.
    public async Task<List<ProductResponse>> GetPagedAsync(Guid userId, int skip, int take)
    {
        var products = await dbContext.Products
            .Where(p => p.UserId == userId && p.IsActive)
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return products.Select(ToResponse).ToList();
    }

    public async Task<ProductResponse> GetByIdAsync(Guid userId, Guid id)
    {
        var product = await FindOwnedActiveProduct(userId, id);

        if (product is null)
        {
            throw new NotFoundException(nameof(Product), id);
        }

        return ToResponse(product);
    }

    public async Task<ProductResponse> UpdateAsync(Guid userId, Guid id, UpdateProductRequest request)
    {
        var product = await FindOwnedActiveProduct(userId, id);

        if (product is null)
        {
            throw new NotFoundException(nameof(Product), id);
        }

        await EnsureBarcodeAvailableAsync(userId, request.Barcode, excludingProductId: id);

        product.Name = request.Name;
        product.Description = request.Description;
        product.ImageUrl = request.ImageUrl;
        product.PurchasePrice = request.PurchasePrice;
        product.SellPrice = request.SellPrice;
        product.Barcode = request.Barcode;

        await dbContext.SaveChangesAsync();

        return ToResponse(product);
    }

    private async Task EnsureBarcodeAvailableAsync(Guid userId, string? barcode, Guid? excludingProductId)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return;

        var taken = await dbContext.Products.AnyAsync(p =>
            p.UserId == userId && p.Barcode == barcode && p.Id != excludingProductId);

        if (taken)
        {
            throw new ConflictException("هذا الباركود مستخدم مسبقًا لمنتج آخر.");
        }
    }

    public async Task<ProductResponse> UpdateImageAsync(Guid userId, Guid id, string imageUrl)
    {
        var product = await FindOwnedActiveProduct(userId, id);

        if (product is null)
        {
            throw new NotFoundException(nameof(Product), id);
        }

        product.ImageUrl = imageUrl;
        await dbContext.SaveChangesAsync();

        return ToResponse(product);
    }

    public async Task<ProductResponse> DeleteImageAsync(Guid userId, Guid id)
    {
        var product = await FindOwnedActiveProduct(userId, id);

        if (product is null)
        {
            throw new NotFoundException(nameof(Product), id);
        }

        product.ImageUrl = null;
        await dbContext.SaveChangesAsync();

        return ToResponse(product);
    }

    public async Task DeleteAsync(Guid userId, Guid id)
    {
        var product = await FindOwnedActiveProduct(userId, id);

        if (product is null)
        {
            throw new NotFoundException(nameof(Product), id);
        }

        product.IsActive = false;
        await dbContext.SaveChangesAsync();
    }

    public async Task<List<ProductResponse>> GetArchivedAsync(Guid userId)
    {
        var products = await dbContext.Products
            .Where(p => p.UserId == userId && !p.IsActive)
            .ToListAsync();

        return products.Select(ToResponse).ToList();
    }

    public async Task<ProductResponse> RestoreAsync(Guid userId, Guid id)
    {
        var product = await FindOwnedProduct(userId, id);

        if (product is null)
        {
            throw new NotFoundException(nameof(Product), id);
        }

        product.IsActive = true;
        await dbContext.SaveChangesAsync();

        return ToResponse(product);
    }

    public async Task PermanentDeleteAsync(Guid userId, Guid id)
    {
        var product = await FindOwnedProduct(userId, id);

        if (product is null)
        {
            throw new NotFoundException(nameof(Product), id);
        }

        dbContext.Products.Remove(product);
        await dbContext.SaveChangesAsync();
    }

    private async Task<Product?> FindOwnedActiveProduct(Guid userId, Guid id) =>
        await dbContext.Products.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId && p.IsActive);

    private async Task<Product?> FindOwnedProduct(Guid userId, Guid id) =>
        await dbContext.Products.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

    internal static ProductResponse ToResponse(Product product) => new()
    {
        Id = product.Id,
        Name = product.Name,
        Description = product.Description,
        ImageUrl = product.ImageUrl,
        Barcode = product.Barcode,
        PurchasePrice = product.PurchasePrice,
        SellPrice = product.SellPrice,
        IsActive = product.IsActive,
        CreatedAt = product.CreatedAt,
        UpdatedAt = product.UpdatedAt
    };
}
