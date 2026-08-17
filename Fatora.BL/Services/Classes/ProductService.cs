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
    public async Task<ProductResponse> CreateAsync(Guid userId, CreateProductRequest request, Guid? createdByRepId = null)
    {
        // Selected is the one mode a Rep can never create in - its whole
        // catalog is an owner-curated, closed list, and letting one create
        // products anyway would just silently add rows to the owner's
        // shared catalog with no picker (RepProductAccess) granting them
        // back to that same Rep. RepOwnedOnly and All both allow it: a
        // RepOwnedOnly product IS the Rep's catalog, and an All-mode Rep
        // creating one is no different from the owner doing it themselves.
        // Checked here (not just hidden in the UI) since this is a real
        // permission boundary, not a display preference.
        if (createdByRepId is not null)
        {
            var rep = await dbContext.Reps.FirstOrDefaultAsync(r => r.Id == createdByRepId);
            if (rep is null || rep.ProductAccessMode == ProductAccessMode.Selected)
            {
                throw new ForbiddenException("لا يمكن إنشاء أصناف في وضع الأصناف المحددة.");
            }
        }

        await EnsureBarcodeAvailableAsync(userId, request.Barcode, excludingProductId: null);

        var product = new Product
        {
            Name = request.Name,
            Description = request.Description,
            ImageUrl = request.ImageUrl,
            PurchasePrice = request.PurchasePrice,
            SellPrice = request.SellPrice,
            Barcode = request.Barcode,
            StockQuantity = request.StockQuantity,
            UserId = userId,
            CreatedByRepId = createdByRepId
        };

        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync();

        return ToResponse(product);
    }

    public async Task<List<ProductResponse>> GetAllAsync(Guid userId, Guid? scopeToRepId = null)
    {
        var query = dbContext.Products.Where(p => p.UserId == userId && p.IsActive);
        query = await ApplyRepScopeAsync(query, scopeToRepId);

        var products = await query.OrderByDescending(p => p.CreatedAt).ToListAsync();

        return products.Select(ToResponse).ToList();
    }

    // Newest-first, with a stable sort so Skip/Take means the same "page"
    // on every call.
    public async Task<List<ProductResponse>> GetPagedAsync(Guid userId, int skip, int take, Guid? scopeToRepId = null)
    {
        var query = dbContext.Products.Where(p => p.UserId == userId && p.IsActive);
        query = await ApplyRepScopeAsync(query, scopeToRepId);

        var products = await query.OrderByDescending(p => p.CreatedAt).Skip(skip).Take(take).ToListAsync();

        return products.Select(ToResponse).ToList();
    }

    public async Task<ProductResponse> GetByIdAsync(Guid userId, Guid id, Guid? scopeToRepId = null)
    {
        var product = await FindOwnedActiveProduct(userId, id);

        if (product is null || !await IsVisibleToRepAsync(id, scopeToRepId))
        {
            throw new NotFoundException(nameof(Product), id);
        }

        return ToResponse(product);
    }

    public async Task<ProductResponse> UpdateAsync(Guid userId, Guid id, UpdateProductRequest request, Guid? scopeToRepId = null)
    {
        var product = await FindOwnedActiveProduct(userId, id);

        if (product is null)
        {
            throw new NotFoundException(nameof(Product), id);
        }

        var isOwnRepCreation = IsOwnRepCreation(product, scopeToRepId);
        Rep? callerRep = null;
        if (!isOwnRepCreation)
        {
            callerRep = await dbContext.Reps.FirstOrDefaultAsync(r => r.Id == scopeToRepId);
            if (callerRep is null || !callerRep.CanEditStock ||
                !await IsVisibleToRepAsync(id, scopeToRepId))
            {
                throw new NotFoundException(nameof(Product), id);
            }

            // Stock access never grants catalog editing. For a visible
            // product created by someone else, change the quantity only.
            product.StockQuantity = request.StockQuantity;
            await dbContext.SaveChangesAsync();
            return ToResponse(product);
        }

        await EnsureBarcodeAvailableAsync(userId, request.Barcode, excludingProductId: id);

        product.Name = request.Name;
        product.Description = request.Description;
        product.ImageUrl = request.ImageUrl;
        product.PurchasePrice = request.PurchasePrice;
        product.SellPrice = request.SellPrice;
        product.Barcode = request.Barcode;

        // A Rep whose CanEditStock is off may still edit everything else
        // about a product it's otherwise allowed to touch (see
        // IsOwnRepCreation above) - this is narrower than the create/edit
        // boundary, so the quantity in the request is silently ignored
        // rather than rejecting the whole save. The owner (scopeToRepId is
        // null) is never subject to this at all.
        if (scopeToRepId is null || request.StockQuantity == product.StockQuantity)
        {
            product.StockQuantity = request.StockQuantity;
        }
        else
        {
            callerRep ??= await dbContext.Reps.FirstOrDefaultAsync(r => r.Id == scopeToRepId);
            if (callerRep is null || callerRep.CanEditStock)
            {
                product.StockQuantity = request.StockQuantity;
            }
        }

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

    public async Task DeleteAsync(Guid userId, Guid id, Guid? scopeToRepId = null)
    {
        var product = await FindOwnedActiveProduct(userId, id);

        if (product is null || !IsOwnRepCreation(product, scopeToRepId))
        {
            throw new NotFoundException(nameof(Product), id);
        }

        product.IsActive = false;
        await dbContext.SaveChangesAsync();
    }

    public async Task<List<ProductResponse>> GetArchivedAsync(Guid userId, Guid? scopeToRepId = null)
    {
        var query = dbContext.Products.Where(p => p.UserId == userId && !p.IsActive);
        query = await ApplyRepScopeAsync(query, scopeToRepId);

        var products = await query.ToListAsync();

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

        // OrderItems.ProductId -> Products is configured to cascade (see
        // FK_OrderItems_Products_ProductId) - the same class of risk
        // CustomerService.PermanentDeleteAsync already guards against for
        // Orders/Receipts. An unconditional Remove here would silently
        // take every past invoice's line item for this product with it.
        // Archiving (DeleteAsync) is the supported way to retire a
        // product that has ever actually been sold; a permanent delete
        // stays available only for one that never traded.
        var hasHistory = await dbContext.OrderItems.AnyAsync(oi => oi.ProductId == id);

        if (hasHistory)
        {
            throw new ConflictException("لا يمكن حذف صنف له فواتير سابقة نهائيًا - اجعله غير متوفر بدلًا من ذلك.");
        }

        dbContext.Products.Remove(product);
        await dbContext.SaveChangesAsync();
    }

    private async Task<Product?> FindOwnedActiveProduct(Guid userId, Guid id) =>
        await dbContext.Products.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId && p.IsActive);

    // Null scopeToRepId (the owner) never filters anything. The three
    // ProductAccessMode values are mutually exclusive, not layered:
    // RepOwnedOnly sees only what it created itself, Selected sees only its
    // RepProductAccess grants, All sees everything - unlike the old
    // Restricted mode, a Selected Rep's own creations don't factor in at
    // all, since Selected Reps can never create one in the first place (see
    // CreateAsync).
    private async Task<IQueryable<Product>> ApplyRepScopeAsync(IQueryable<Product> query, Guid? scopeToRepId)
    {
        if (scopeToRepId is null) return query;

        var rep = await dbContext.Reps.FirstOrDefaultAsync(r => r.Id == scopeToRepId);
        if (rep is null || rep.ProductAccessMode == ProductAccessMode.All) return query;

        if (rep.ProductAccessMode == ProductAccessMode.RepOwnedOnly)
        {
            return query.Where(p => p.CreatedByRepId == scopeToRepId);
        }

        var allowedIds = dbContext.RepProductAccesses
            .Where(a => a.RepId == scopeToRepId)
            .Select(a => a.ProductId);
        return query.Where(p => allowedIds.Contains(p.Id));
    }

    private async Task<bool> IsVisibleToRepAsync(Guid productId, Guid? scopeToRepId)
    {
        if (scopeToRepId is null) return true;

        var rep = await dbContext.Reps.FirstOrDefaultAsync(r => r.Id == scopeToRepId);
        if (rep is null || rep.ProductAccessMode == ProductAccessMode.All) return true;

        if (rep.ProductAccessMode == ProductAccessMode.RepOwnedOnly)
        {
            return await dbContext.Products.AnyAsync(p => p.Id == productId && p.CreatedByRepId == scopeToRepId);
        }

        return await dbContext.RepProductAccesses.AnyAsync(a => a.RepId == scopeToRepId && a.ProductId == productId);
    }

    // Edit/delete needs the stricter "did this Rep create it" check, not
    // the broader read-visibility one above - a Restricted Rep may be able
    // to see a product via a RepProductAccess grant without having created
    // it, and must never be able to touch someone else's catalog entry.
    private static bool IsOwnRepCreation(Product product, Guid? scopeToRepId) =>
        scopeToRepId is null || product.CreatedByRepId == scopeToRepId;

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
        StockQuantity = product.StockQuantity,
        IsActive = product.IsActive,
        CreatedAt = product.CreatedAt,
        UpdatedAt = product.UpdatedAt,
        CreatedByRepId = product.CreatedByRepId
    };
}
