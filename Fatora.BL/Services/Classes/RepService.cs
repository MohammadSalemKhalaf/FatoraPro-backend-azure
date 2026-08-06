using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace Fatora.BL.Services.Classes;

// Owner-facing CRUD for Reps - never called by a Rep session itself, only
// by the SalesRep-tier owner (see RepsController's [Authorize(Roles =
// "SalesRep")]). Session issuing/refresh/revocation lives in
// IRepAuthService instead, mirroring how LoginService/JwtTokenProviderService
// are split for normal Users.
public class RepService(AppDbContext dbContext, IRepAuthService repAuthService) : IRepService
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

    public async Task<List<RepResponse>> GetAllAsync(Guid ownerUserId)
    {
        var reps = await dbContext.Reps
            .Where(r => r.OwnerUserId == ownerUserId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return reps.Select(r => ToResponse(r, includeQrToken: false)).ToList();
    }

    public async Task LogoutAsync(Guid ownerUserId, Guid repId)
    {
        var rep = await FindOwnedRep(ownerUserId, repId);
        await repAuthService.RevokeSessionAsync(rep.Id);
    }

    public async Task DeactivateAsync(Guid ownerUserId, Guid repId)
    {
        var rep = await FindOwnedRep(ownerUserId, repId);

        rep.IsActive = false;
        await dbContext.SaveChangesAsync();

        // Blocks any future login-by-qr immediately - a lingering active
        // session still gets force-ended here too, rather than left to
        // expire naturally over its remaining refresh-token lifetime.
        await repAuthService.RevokeSessionAsync(rep.Id);
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
        ProductAccessMode = rep.ProductAccessMode.ToString(),
        CustomerAccessMode = rep.CustomerAccessMode.ToString(),
        CreatedAt = rep.CreatedAt
    };
}
