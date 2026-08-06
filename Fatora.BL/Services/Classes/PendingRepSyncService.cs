using System.Text.Json;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;

namespace Fatora.BL.Services.Classes;

public class PendingRepSyncService(AppDbContext dbContext, ISyncService syncService) : IPendingRepSyncService
{
    public async Task UploadAsync(Guid repId, SyncPushRequest request)
    {
        // At most one queued batch per rep (RepId is unique-indexed) - a
        // retry, or a second reconnect that still finds itself blocked,
        // simply replaces whatever was queued before, since it represents
        // that same device's full current dirty set, not an incremental
        // delta on top of the last one.
        var existing = await dbContext.PendingRepSyncs.FirstOrDefaultAsync(p => p.RepId == repId);
        if (existing is not null)
        {
            dbContext.PendingRepSyncs.Remove(existing);
        }

        dbContext.PendingRepSyncs.Add(new PendingRepSync
        {
            RepId = repId,
            PayloadJson = JsonSerializer.Serialize(request),
            CustomerCount = request.Customers.Count,
            ProductCount = request.Products.Count,
            OrderCount = request.Orders.Count,
            ReceiptCount = request.Receipts.Count,
            CreatedAt = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();
    }

    public async Task<PendingRepSyncSummaryResponse> GetSummaryAsync(Guid ownerUserId, Guid repId)
    {
        await EnsureOwnedRepAsync(ownerUserId, repId);

        var pending = await dbContext.PendingRepSyncs.AsNoTracking().FirstOrDefaultAsync(p => p.RepId == repId);
        if (pending is null)
        {
            return new PendingRepSyncSummaryResponse { HasPendingSync = false };
        }

        return new PendingRepSyncSummaryResponse
        {
            HasPendingSync = true,
            CustomerCount = pending.CustomerCount,
            ProductCount = pending.ProductCount,
            OrderCount = pending.OrderCount,
            ReceiptCount = pending.ReceiptCount,
            CreatedAt = pending.CreatedAt
        };
    }

    public async Task<SyncPushResponse> ApplyAsync(Guid ownerUserId, Guid repId)
    {
        await EnsureOwnedRepAsync(ownerUserId, repId);

        var pending = await dbContext.PendingRepSyncs.FirstOrDefaultAsync(p => p.RepId == repId);
        if (pending is null)
        {
            throw new NotFoundException(nameof(PendingRepSync), repId);
        }

        // Reuses the exact same push path a live Rep session's own sync
        // already goes through - invoice numbering, stock adjustment,
        // conflict detection, access-mode validation, all of it - so
        // applying a queued batch can never disagree with what a normal,
        // still-active sync would have done with the same data.
        var request = JsonSerializer.Deserialize<SyncPushRequest>(pending.PayloadJson)!;
        var result = await syncService.PushAsync(ownerUserId, request, scopeToRepId: repId);

        dbContext.PendingRepSyncs.Remove(pending);
        await dbContext.SaveChangesAsync();

        return result;
    }

    public async Task DiscardAsync(Guid ownerUserId, Guid repId)
    {
        await EnsureOwnedRepAsync(ownerUserId, repId);

        var pending = await dbContext.PendingRepSyncs.FirstOrDefaultAsync(p => p.RepId == repId);
        if (pending is null)
        {
            throw new NotFoundException(nameof(PendingRepSync), repId);
        }

        dbContext.PendingRepSyncs.Remove(pending);
        await dbContext.SaveChangesAsync();
    }

    private async Task EnsureOwnedRepAsync(Guid ownerUserId, Guid repId)
    {
        var owned = await dbContext.Reps.AnyAsync(r => r.Id == repId && r.OwnerUserId == ownerUserId);
        if (!owned)
        {
            throw new NotFoundException(nameof(Rep), repId);
        }
    }
}
