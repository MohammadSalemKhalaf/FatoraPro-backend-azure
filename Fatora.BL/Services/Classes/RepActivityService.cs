using Fatora.BL.DTOs.Responses;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;

namespace Fatora.BL.Services.Classes;

public class RepActivityService(AppDbContext dbContext) : IRepActivityService
{
    public async Task<RepRouteResponse> GetRouteAsync(
        Guid ownerUserId,
        Guid repId,
        DateTime startUtc,
        DateTime endUtc
    )
    {
        await EnsureOwnedRepAsync(ownerUserId, repId);

        // CreatedAt is `timestamp with time zone` in Postgres - Npgsql
        // refuses an Unspecified-kind DateTime against it, so these need
        // to be explicitly stamped UTC regardless of how the model binder
        // parsed the incoming query string (CreatedAt itself is always
        // stored as UTC - see every ...CreatedAt = DateTime.UtcNow
        // assignment elsewhere in this service layer).
        var startOfDay = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        var endOfDay = DateTime.SpecifyKind(endUtc, DateTimeKind.Utc);

        // Not filtered on Order.IsDeleted - that flag only ever hides an
        // invoice from the owner's Invoices list, every other view (reports,
        // exports, and this one) keeps reading it exactly as if it were
        // never deleted. See Order.IsDeleted.
        var orders = await dbContext.Orders
            .AsNoTracking()
            .Where(o => o.CreatedByRepId == repId && o.CreatedAt >= startOfDay && o.CreatedAt < endOfDay)
            .Select(o => new
            {
                o.Id,
                o.Latitude,
                o.Longitude,
                o.CreatedAt,
                o.InvoiceNumber,
                CustomerName = o.Customer.Name
            })
            .ToListAsync();

        var receipts = await dbContext.Receipts
            .AsNoTracking()
            .Where(r => r.CreatedByRepId == repId && r.IsActive && r.CreatedAt >= startOfDay && r.CreatedAt < endOfDay)
            .Select(r => new
            {
                r.Id,
                r.Latitude,
                r.Longitude,
                r.CreatedAt,
                r.Amount,
                CustomerName = r.Customer.Name
            })
            .ToListAsync();

        // Same "preparing"/"ready" carve-out as GetActivityFeedAsync below -
        // a draft is still being built by the rep, not activity worth
        // plotting yet. PurchaseRequest has no Customer navigation property
        // (see GetActivityFeedAsync's comment), so names are resolved with a
        // separate lookup instead of an Include.
        //
        // Windowed on SyncedAt, not CreatedAt - same reasoning as
        // GetActivityFeedAsync below. This one specifically matters now that
        // location itself is captured at this same preparing/ready moment
        // (not at creation, see PurchaseRequestsController.Save): a request
        // created one day and only prepared/readied on a later one used to
        // fall into a blind spot on every route - its CreatedAt-day route
        // had no location yet, and the day it actually got one was never
        // checked.
        var purchaseRequests = await dbContext.PurchaseRequests
            .AsNoTracking()
            .Where(pr => pr.CreatedByRepId == repId
                && (pr.SyncedAt ?? pr.CreatedAt) >= startOfDay && (pr.SyncedAt ?? pr.CreatedAt) < endOfDay
                && (pr.Status == "preparing" || pr.Status == "ready"))
            .Select(pr => new
            {
                pr.Id,
                pr.Latitude,
                pr.Longitude,
                CreatedAt = pr.SyncedAt ?? pr.CreatedAt,
                pr.CustomerId
            })
            .ToListAsync();
        var requestCustomerIds = purchaseRequests
            .Where(pr => pr.CustomerId != null)
            .Select(pr => pr.CustomerId!.Value)
            .Distinct()
            .ToList();
        var requestCustomerNames = await dbContext.Customers
            .AsNoTracking()
            .Where(c => requestCustomerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name);

        var unlocatedCount = orders.Count(o => o.Latitude is null)
            + receipts.Count(r => r.Latitude is null)
            + purchaseRequests.Count(pr => pr.Latitude is null);

        var points = orders
            .Where(o => o.Latitude is not null && o.Longitude is not null)
            .Select(o => new RepRoutePointResponse
            {
                Id = o.Id,
                Type = "Order",
                Latitude = o.Latitude!.Value,
                Longitude = o.Longitude!.Value,
                CustomerName = o.CustomerName,
                Label = o.InvoiceNumber,
                CreatedAt = o.CreatedAt
            })
            .Concat(receipts
                .Where(r => r.Latitude is not null && r.Longitude is not null)
                .Select(r => new RepRoutePointResponse
                {
                    Id = r.Id,
                    Type = "Receipt",
                    Latitude = r.Latitude!.Value,
                    Longitude = r.Longitude!.Value,
                    CustomerName = r.CustomerName,
                    Label = r.Amount.ToString("0.##"),
                    CreatedAt = r.CreatedAt
                }))
            .Concat(purchaseRequests
                .Where(pr => pr.Latitude is not null && pr.Longitude is not null)
                .Select(pr => new RepRoutePointResponse
                {
                    Id = pr.Id,
                    Type = "PurchaseRequest",
                    Latitude = pr.Latitude!.Value,
                    Longitude = pr.Longitude!.Value,
                    CustomerName = pr.CustomerId != null &&
                        requestCustomerNames.TryGetValue(pr.CustomerId.Value, out var customerName)
                        ? customerName
                        : "بدون عميل",
                    Label = "طلبية",
                    CreatedAt = pr.CreatedAt
                }))
            .OrderBy(p => p.CreatedAt)
            .ToList();

        for (var i = 0; i < points.Count; i++)
        {
            points[i].Sequence = i + 1;
        }

        return new RepRouteResponse { Points = points, UnlocatedCount = unlocatedCount };
    }

    public async Task<List<RepActivityItemResponse>> GetActivityFeedAsync(Guid ownerUserId)
    {
        var since = DateTime.UtcNow.AddHours(-24);

        // Loaded with Includes and mapped client-side (not a direct
        // .Select(... => new RepActivityItemResponse { Amount = o.Total })
        // projection) because Order.Total is a computed property built from
        // OrderItems/Math.Round, the same reason OrderService.ToResponse
        // materializes entities first - see that method.
        var orders = await dbContext.Orders
            .AsNoTracking()
            .Include(o => o.OrderItems)
            .Include(o => o.Customer)
            .Include(o => o.CreatedByRep)
            // A rep who worked offline for a while syncs with a CreatedAt
            // already stamped from the device's clock at creation time -
            // comparing that against the window would drop it even though
            // the sync itself just succeeded. SyncedAt (set once, at the
            // moment the row actually reached the server - see
            // SyncService.PushOrderAsync) is what "recent" should mean
            // here; CreatedAt is only a fallback for rows synced before
            // that column existed.
            .Where(o => o.UserId == ownerUserId && o.CreatedByRepId != null
                && (o.SyncedAt ?? o.CreatedAt) >= since)
            .ToListAsync();

        var receipts = await dbContext.Receipts
            .AsNoTracking()
            .Include(r => r.Customer)
            .Include(r => r.CreatedByRep)
            .Where(r => r.UserId == ownerUserId && r.CreatedByRepId != null && r.IsActive
                && (r.SyncedAt ?? r.CreatedAt) >= since)
            .ToListAsync();

        // Only requests actually being worked (preparing/ready) - a draft is
        // still being built by the rep and isn't activity worth surfacing to
        // the owner yet. PurchaseRequest has no Customer navigation property
        // (CustomerId is a bare nullable Guid, and can genuinely be null - a
        // request doesn't require a customer the way an Order does), so
        // names are resolved with a separate lookup instead of an Include.
        var purchaseRequests = await dbContext.PurchaseRequests
            .AsNoTracking()
            .Include(pr => pr.CreatedByRep)
            .Where(pr => pr.UserId == ownerUserId && pr.CreatedByRepId != null
                && (pr.SyncedAt ?? pr.CreatedAt) >= since
                && (pr.Status == "preparing" || pr.Status == "ready"))
            .ToListAsync();

        var requestCustomerIds = purchaseRequests
            .Where(pr => pr.CustomerId != null)
            .Select(pr => pr.CustomerId!.Value)
            .Distinct()
            .ToList();
        var requestCustomerNames = await dbContext.Customers
            .AsNoTracking()
            .Where(c => requestCustomerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name);

        return orders
            .Select(o => new RepActivityItemResponse
            {
                Id = o.Id,
                RepId = o.CreatedByRepId!.Value,
                RepName = o.CreatedByRep!.Name,
                Type = "Order",
                CustomerName = o.Customer.Name,
                Detail = o.InvoiceNumber,
                Amount = o.Total,
                CreatedAt = o.CreatedAt,
                Latitude = o.Latitude,
                Longitude = o.Longitude
            })
            .Concat(
                receipts.Select(r => new RepActivityItemResponse
                {
                    Id = r.Id,
                    RepId = r.CreatedByRepId!.Value,
                    RepName = r.CreatedByRep!.Name,
                    Type = "Receipt",
                    CustomerName = r.Customer.Name,
                    Detail = string.Empty,
                    Amount = r.Amount,
                    CreatedAt = r.CreatedAt,
                    Latitude = r.Latitude,
                    Longitude = r.Longitude
                })
            )
            .Concat(
                purchaseRequests.Select(pr => new RepActivityItemResponse
                {
                    Id = pr.Id,
                    RepId = pr.CreatedByRepId!.Value,
                    RepName = pr.CreatedByRep!.Name,
                    Type = "PurchaseRequest",
                    CustomerName = pr.CustomerId != null &&
                        requestCustomerNames.TryGetValue(pr.CustomerId.Value, out var customerName)
                        ? customerName
                        : "بدون عميل",
                    // The raw status ("preparing"/"ready") - the client
                    // already owns the Arabic label mapping for this exact
                    // enum (see PurchaseRequestStatusLabel), so this stays a
                    // plain data field rather than pre-formatted text.
                    Detail = pr.Status,
                    Amount = 0,
                    CreatedAt = pr.CreatedAt,
                    Latitude = pr.Latitude,
                    Longitude = pr.Longitude
                })
            )
            .OrderByDescending(item => item.CreatedAt)
            .ToList();
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
