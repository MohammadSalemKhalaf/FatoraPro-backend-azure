using Fatora.BL.DTOs.Responses;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;

namespace Fatora.BL.Services.Classes;

// Read-only: unlike Customer/Product, there is no Create/Update/Delete here
// - a receipt is only ever created/updated/soft-deleted through the sync
// protocol (see SyncService.PushReceiptAsync), matching how the client
// always writes offline-first. This GetAllAsync exists purely to back the
// same full-refresh pull-to-refresh pattern CustomersRepository/
// OrdersRepository already use, separate from SyncManager's delta pull.
public class ReceiptService(AppDbContext dbContext) : IReceiptService
{
    public async Task<List<ReceiptResponse>> GetAllAsync(Guid userId)
    {
        var receipts = await dbContext.Receipts
            .Where(r => r.UserId == userId && r.IsActive)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return receipts.Select(ToResponse).ToList();
    }

    internal static ReceiptResponse ToResponse(Receipt receipt) => new()
    {
        Id = receipt.Id,
        CustomerId = receipt.CustomerId,
        Amount = receipt.Amount,
        IsActive = receipt.IsActive,
        CreatedAt = receipt.CreatedAt,
        UpdatedAt = receipt.UpdatedAt
    };
}
