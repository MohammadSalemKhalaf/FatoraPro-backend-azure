using Fatora.API.Extensions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fatora.API.Controllers;

[Route("api/purchase-requests")]
[ApiController]
[Authorize(Roles = "SalesRep,Rep")]
public class PurchaseRequestsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? repId)
    {
        var ownerId = User.GetEffectiveOwnerId();
        var scope = User.GetRepIdOrNull() ?? repId;
        var query = db.PurchaseRequests.AsNoTracking()
            .Include(x => x.CreatedByRep)
            .Include(x => x.Items)
            .Where(x => x.UserId == ownerId);
        if (scope is not null) query = query.Where(x => x.CreatedByRepId == scope);
        var rows = await query.OrderByDescending(x => x.UpdatedAt).ToListAsync();
        return Ok(rows.Select(ToResponse));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Save(Guid id, SavePurchaseRequest request)
    {
        if (id != request.Id || request.Items.Any(x => x.Quantity <= 0)) return BadRequest();
        if (request.Status is not ("draft" or "preparing" or "ready")) return BadRequest();
        var ownerId = User.GetEffectiveOwnerId();
        var repId = User.GetRepIdOrNull();
        var entity = await db.PurchaseRequests.Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == ownerId);
        if (entity is not null && repId is not null && entity.CreatedByRepId != repId) return Forbid();
        // Ready means an invoice was already created. From this point the
        // request is an immutable audit record; even a stale device must not
        // be able to alter its customer, lines, notes, or state.
        if (entity is not null && entity.Status == "ready")
            return Ok(ToResponse(entity));
        if (entity is not null && entity.UpdatedAt > request.UpdatedAt.ToUniversalTime())
            return Ok(ToResponse(entity));

        if (entity is null)
        {
            entity = new PurchaseRequest { Id = id, UserId = ownerId, CreatedByRepId = repId,
                CreatedAt = request.CreatedAt.ToUniversalTime() };
            db.PurchaseRequests.Add(entity);
        }
        // Set once, whenever the device first actually has a value to offer -
        // never at draft (the app no longer even tries then), typically the
        // preparing/ready transition instead. Never overwritten once set,
        // same "set once" semantics as Order/Receipt, just not tied to the
        // literal creation branch above since that's no longer when it
        // first arrives.
        if (entity.Latitude is null && entity.Longitude is null)
        {
            entity.Latitude = request.Latitude;
            entity.Longitude = request.Longitude;
        }
        entity.CustomerId = request.CustomerId;
        entity.Status = request.Status;
        entity.Notes = request.Notes;
        entity.InvoiceId = request.InvoiceId;
        entity.UpdatedAt = request.UpdatedAt.ToUniversalTime();
        // Unlike Order/Receipt's SyncedAt (frozen at creation - see
        // SyncService.PushOrderAsync), a purchase request is often drafted
        // long before it becomes real activity: it may sit as "draft" for
        // a while, then only turn "preparing"/"ready" on a later save. The
        // owner's activity feed cares about *that* moment, not the
        // original draft's - so this is stamped on every save, not just
        // creation.
        entity.SyncedAt = DateTime.UtcNow;
        // Managed directly through the DbSet (not via the entity.Items navigation setter) -
        // replacing a required collection navigation by reassigning it confuses EF's change
        // tracker into generating an UPDATE against the row it just DELETEd instead of an
        // INSERT, throwing a DbUpdateConcurrencyException ("expected to affect 1 row(s), but
        // actually affected 0"). Identical comment/fix in SyncService.PushOrderAsync's edit
        // branch for Order.OrderItems - same anti-pattern, just never ported over here.
        db.PurchaseRequestItems.RemoveRange(entity.Items);
        var newItems = request.Items.Select(x => new PurchaseRequestItem {
            PurchaseRequestId = id, ProductId = x.ProductId, Quantity = x.Quantity }).ToList();
        db.PurchaseRequestItems.AddRange(newItems);
        await db.SaveChangesAsync();
        // Safe here (unlike before SaveChangesAsync above) - the delete/insert has already
        // committed, so this is just making sure the in-memory entity reflects it for the
        // response below, not fighting the change tracker mid-flight.
        entity.Items = newItems;
        await db.Entry(entity).Reference(x => x.CreatedByRep).LoadAsync();
        return Ok(ToResponse(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var ownerId = User.GetEffectiveOwnerId();
        var repId = User.GetRepIdOrNull();
        var entity = await db.PurchaseRequests.FirstOrDefaultAsync(x => x.Id == id && x.UserId == ownerId);
        if (entity is null) return NoContent();
        if (repId is not null && entity.CreatedByRepId != repId) return Forbid();
        db.PurchaseRequests.Remove(entity);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static object ToResponse(PurchaseRequest x) => new {
        x.Id, x.CustomerId, x.Status, x.Notes, x.InvoiceId, x.CreatedAt, x.UpdatedAt,
        createdByRepId = x.CreatedByRepId, createdByRepName = x.CreatedByRep?.Name,
        x.Latitude, x.Longitude,
        items = x.Items.Select(i => new { i.ProductId, i.Quantity })
    };
}

public record SavePurchaseRequest(Guid Id, Guid? CustomerId, string Status, string? Notes,
    Guid? InvoiceId, DateTime CreatedAt, DateTime UpdatedAt, List<SavePurchaseRequestItem> Items,
    double? Latitude = null, double? Longitude = null);
public record SavePurchaseRequestItem(Guid ProductId, int Quantity);
