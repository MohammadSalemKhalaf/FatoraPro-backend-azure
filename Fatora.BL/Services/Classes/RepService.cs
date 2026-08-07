using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace Fatora.BL.Services.Classes;

// Owner-facing CRUD for Reps - only by the SalesRep-tier owner (see
// RepsController's [Authorize(Roles = "SalesRep")]), with one exception:
// GetMyInfoAsync, which a Rep session calls about itself (to know its own
// current ProductAccessMode - e.g. whether to offer "create my own
// product"), needs no owner-ownership check since the caller already IS the
// rep in question. Session issuing/refresh lives in IRepAuthService instead,
// mirroring how LoginService/JwtTokenProviderService are split for normal
// Users - this class only ever changes SessionVersion/IsActive, never talks
// to IRepAuthService directly.
public class RepService(AppDbContext dbContext) : IRepService
{
    public async Task<RepResponse> CreateAsync(Guid ownerUserId, CreateRepRequest request)
    {
        var rep = new Rep
        {
            OwnerUserId = ownerUserId,
            Name = request.Name,
            QrToken = GenerateQrToken(),
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Reps.Add(rep);
        await dbContext.SaveChangesAsync();

        return ToResponse(rep, includeQrToken: true);
    }

    // includeHidden: false (the management list's default) is what keeps a
    // deleted rep out of the owner's cluttered "إدارة المندوبين" view - true
    // is for the separate "filter by rep" picker (RepFilterRow), which still
    // needs to resolve a deleted rep by design, since its historical
    // invoices/customers/receipts remain fully filterable. See Rep.IsHidden.
    public async Task<List<RepResponse>> GetAllAsync(Guid ownerUserId, bool includeHidden = false)
    {
        var reps = await dbContext.Reps
            .Where(r => r.OwnerUserId == ownerUserId && (includeHidden || !r.IsHidden))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return reps.Select(r => ToResponse(r, includeQrToken: false)).ToList();
    }

    // Unlike GetAllAsync, this includes the raw QrToken - a plain list never
    // does (see ToResponse), but the owner deliberately opening one rep's
    // own detail screen (to re-share its QR after a lost device, say) is
    // exactly the on-demand case that token exposure should be limited to.
    public async Task<RepResponse> GetByIdAsync(Guid ownerUserId, Guid repId)
    {
        var rep = await FindOwnedRep(ownerUserId, repId);
        return ToResponse(rep, includeQrToken: true);
    }

    public async Task<RepResponse> GetMyInfoAsync(Guid repId)
    {
        var rep = await dbContext.Reps.FirstOrDefaultAsync(r => r.Id == repId);
        if (rep is null)
        {
            throw new NotFoundException(nameof(Rep), repId);
        }

        return ToResponse(rep, includeQrToken: false);
    }

    public async Task LogoutAsync(Guid ownerUserId, Guid repId)
    {
        var rep = await FindOwnedRep(ownerUserId, repId);

        // SessionVersion++ is what actually force-ends an already-issued
        // access token mid-session (see AccountStatusFilter), and - since
        // RepRefreshToken now snapshots the SessionVersion it was issued
        // under (see RepAuthService.IssueRefreshTokenAsync) - it's also
        // enough on its own to reject the next refresh attempt with the
        // dedicated REP_SESSION_ENDED code (see TryRefreshAsync). Explicitly
        // NOT deleting the refresh-token row here (unlike an earlier version
        // of this method): doing so destroyed the one thing that rejection
        // needed to still find, so a device whose access token had already
        // expired by the time it reconnected got a generic, code-less
        // rejection instead - silently kicked out with no explanation,
        // exactly the popup this whole mechanism exists to prevent. Mirrors
        // DeactivateAsync's identical reasoning below.
        rep.SessionVersion++;
        await dbContext.SaveChangesAsync();
    }

    public async Task DeactivateAsync(Guid ownerUserId, Guid repId)
    {
        var rep = await FindOwnedRep(ownerUserId, repId);

        rep.IsActive = false;
        rep.SessionVersion++;
        await dbContext.SaveChangesAsync();

        // Deliberately does NOT delete the refresh-token row - IsActive=false
        // already blocks a future login-by-qr, and the row surviving is what
        // lets TryRefreshAsync tell "this rep was deactivated" apart from
        // "this token never existed" and return the dedicated,
        // REP_SESSION_ENDED-coded rejection instead of a generic one.
    }

    // Distinct from DeactivateAsync: this is the owner "letting a rep go" for
    // good - it also blocks login (IsActive=false, SessionVersion++, exactly
    // like deactivation) but additionally hides it from the management list
    // (see Rep.IsHidden / GetAllAsync's includeHidden param). Every Order/
    // Customer/Product/Receipt it created is untouched and keeps resolving -
    // the rep row itself only ever disappears for real once
    // AnnualInventoryService's post-wipe sweep finds nothing left pointing
    // at it. There is deliberately no "undelete" - matches Order.IsDeleted's
    // same one-way design.
    public async Task DeleteAsync(Guid ownerUserId, Guid repId)
    {
        var rep = await FindOwnedRep(ownerUserId, repId);

        rep.IsActive = false;
        rep.IsHidden = true;
        rep.SessionVersion++;
        await dbContext.SaveChangesAsync();
    }

    public async Task<RepResponse> ReactivateAsync(Guid ownerUserId, Guid repId)
    {
        var rep = await FindOwnedRep(ownerUserId, repId);

        rep.IsActive = true;
        await dbContext.SaveChangesAsync();

        // The QrToken was never touched by DeactivateAsync, so the rep's
        // existing QR image immediately works again - including it here
        // (like RegenerateQrAsync does) lets the detail screen just take
        // this response as-is instead of making a second GetByIdAsync call.
        return ToResponse(rep, includeQrToken: true);
    }

    public async Task<RepResponse> RegenerateQrAsync(Guid ownerUserId, Guid repId)
    {
        var rep = await FindOwnedRep(ownerUserId, repId);

        rep.QrToken = GenerateQrToken();
        await dbContext.SaveChangesAsync();

        // The old QR is now worthless, but any device already logged in
        // through it stays logged in until its own session naturally
        // expires - regenerating isn't itself a "log everyone out" action.
        return ToResponse(rep, includeQrToken: true);
    }

    public async Task<RepResponse> SetProductAccessAsync(Guid ownerUserId, Guid repId, UpdateRepProductAccessRequest request)
    {
        var rep = await FindOwnedRep(ownerUserId, repId);

        // RepProductAccess rows only ever mean something for Selected mode -
        // RepOwnedOnly's visibility is entirely CreatedByRepId-driven and All
        // sees everything regardless, so switching into (or staying in)
        // either of those always clears any stale grants rather than leaving
        // orphaned rows an admin picker would never show again.
        var existing = await dbContext.RepProductAccesses.Where(a => a.RepId == repId).ToListAsync();
        dbContext.RepProductAccesses.RemoveRange(existing);

        rep.ProductAccessMode = request.Mode;

        if (request.Mode == ProductAccessMode.Selected)
        {
            // Silently drops any id that isn't actually one of this owner's
            // own products - the caller is always the owner themselves, so
            // this is guarding against a stale/mistaken id, not an
            // adversarial one.
            var requestedIds = request.ProductIds ?? [];
            var ownedIds = await dbContext.Products
                .Where(p => p.UserId == ownerUserId && requestedIds.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync();

            dbContext.RepProductAccesses.AddRange(
                ownedIds.Select(productId => new RepProductAccess { RepId = repId, ProductId = productId }));
        }

        await dbContext.SaveChangesAsync();

        return ToResponse(rep, includeQrToken: false);
    }

    public async Task<RepResponse> SetCustomerAccessAsync(Guid ownerUserId, Guid repId, UpdateRepCustomerAccessRequest request)
    {
        var rep = await FindOwnedRep(ownerUserId, repId);

        var existing = await dbContext.RepCustomerAccesses.Where(a => a.RepId == repId).ToListAsync();
        dbContext.RepCustomerAccesses.RemoveRange(existing);

        rep.CustomerAccessMode = request.Mode;

        if (request.Mode == CustomerAccessMode.Restricted)
        {
            // Silently drops any id that isn't actually one of this owner's
            // own customers - the caller is always the owner themselves, so
            // this is guarding against a stale/mistaken id, not an
            // adversarial one. Mirrors SetProductAccessAsync exactly.
            var requestedIds = request.CustomerIds ?? [];
            var ownedIds = await dbContext.Customers
                .Where(c => c.UserId == ownerUserId && requestedIds.Contains(c.Id))
                .Select(c => c.Id)
                .ToListAsync();

            dbContext.RepCustomerAccesses.AddRange(
                ownedIds.Select(customerId => new RepCustomerAccess { RepId = repId, CustomerId = customerId }));
        }

        await dbContext.SaveChangesAsync();

        return ToResponse(rep, includeQrToken: false);
    }

    public async Task<RepAccessListResponse> GetProductAccessAsync(Guid ownerUserId, Guid repId)
    {
        var rep = await FindOwnedRep(ownerUserId, repId);
        var ids = await dbContext.RepProductAccesses
            .Where(a => a.RepId == repId)
            .Select(a => a.ProductId)
            .ToListAsync();

        return new RepAccessListResponse { Mode = rep.ProductAccessMode.ToString(), Ids = ids };
    }

    public async Task<RepAccessListResponse> GetCustomerAccessAsync(Guid ownerUserId, Guid repId)
    {
        var rep = await FindOwnedRep(ownerUserId, repId);
        var ids = await dbContext.RepCustomerAccesses
            .Where(a => a.RepId == repId)
            .Select(a => a.CustomerId)
            .ToListAsync();

        return new RepAccessListResponse { Mode = rep.CustomerAccessMode.ToString(), Ids = ids };
    }

    private async Task<Rep> FindOwnedRep(Guid ownerUserId, Guid repId)
    {
        var rep = await dbContext.Reps.FirstOrDefaultAsync(r => r.Id == repId && r.OwnerUserId == ownerUserId);

        if (rep is null)
        {
            throw new NotFoundException(nameof(Rep), repId);
        }

        return rep;
    }

    private static string GenerateQrToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static RepResponse ToResponse(Rep rep, bool includeQrToken) => new()
    {
        Id = rep.Id,
        Name = rep.Name,
        QrToken = includeQrToken ? rep.QrToken : null,
        IsActive = rep.IsActive,
        IsHidden = rep.IsHidden,
        ProductAccessMode = rep.ProductAccessMode.ToString(),
        CustomerAccessMode = rep.CustomerAccessMode.ToString(),
        CreatedAt = rep.CreatedAt
    };
}
