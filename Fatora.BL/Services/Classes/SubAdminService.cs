using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace Fatora.BL.Services.Classes;

// Admin-facing CRUD for SubAdmins - mirrors RepService, minus everything
// OwnerUserId/access-mode/pending-sync related, since a SubAdmin belongs to
// the platform directly rather than to a specific owner row (there's exactly
// one Admin account platform-wide - see SubAdmin.cs) and its only permission
// surface is the three CanActivate* flags handled by SetPermissionsAsync.
// Session issuing/refresh lives in ISubAdminAuthService instead, mirroring
// how RepService/IRepAuthService are split.
public class SubAdminService(AppDbContext dbContext) : ISubAdminService
{
    public async Task<SubAdminResponse> CreateAsync(CreateSubAdminRequest request)
    {
        var subAdmin = new SubAdmin
        {
            Name = request.Name,
            QrToken = GenerateQrToken(),
            CreatedAt = DateTime.UtcNow
        };

        dbContext.SubAdmins.Add(subAdmin);
        await dbContext.SaveChangesAsync();

        return ToResponse(subAdmin, includeQrToken: true);
    }

    // includeHidden: false (the management list's default) keeps a deleted
    // SubAdmin out of the Admin's "إدارة المندوبين" view - true is for
    // pickers that still need to resolve one (e.g. reports/attribution
    // filters), since its historical activations/attributions remain fully
    // valid. See SubAdmin.IsHidden.
    public async Task<List<SubAdminResponse>> GetAllAsync(bool includeHidden = false)
    {
        var subAdmins = await dbContext.SubAdmins
            .Where(s => includeHidden || !s.IsHidden)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        return subAdmins.Select(s => ToResponse(s, includeQrToken: false)).ToList();
    }

    // Unlike GetAllAsync, this includes the raw QrToken - a plain list never
    // does (see ToResponse), but the Admin deliberately opening one
    // SubAdmin's own detail screen (to re-share its QR after a lost device,
    // say) is exactly the on-demand case that token exposure should be
    // limited to.
    public async Task<SubAdminResponse> GetByIdAsync(Guid subAdminId)
    {
        var subAdmin = await FindSubAdmin(subAdminId);
        return ToResponse(subAdmin, includeQrToken: true);
    }

    public async Task<SubAdminResponse> GetMyInfoAsync(Guid subAdminId)
    {
        var subAdmin = await FindSubAdmin(subAdminId);
        return ToResponse(subAdmin, includeQrToken: false);
    }

    public async Task LogoutAsync(Guid subAdminId)
    {
        var subAdmin = await FindSubAdmin(subAdminId);
        subAdmin.SessionVersion++;
        await dbContext.SaveChangesAsync();
    }

    public async Task DeactivateAsync(Guid subAdminId)
    {
        var subAdmin = await FindSubAdmin(subAdminId);
        subAdmin.IsActive = false;
        subAdmin.SessionVersion++;
        await dbContext.SaveChangesAsync();
    }

    // Distinct from DeactivateAsync: the Admin "letting a SubAdmin go" for
    // good - it also blocks login (IsActive=false, SessionVersion++, exactly
    // like deactivation) but additionally hides it from the management list
    // (see SubAdmin.IsHidden / GetAllAsync's includeHidden param). Every
    // subscriber it ever claimed/activated stays fully untouched - its
    // ManagedBySubAdminId/SubscriptionActivation rows keep resolving
    // regardless (DeleteBehavior.Restrict). There is deliberately no
    // "undelete", matching Rep.IsHidden's same one-way design.
    public async Task DeleteAsync(Guid subAdminId)
    {
        var subAdmin = await FindSubAdmin(subAdminId);
        subAdmin.IsActive = false;
        subAdmin.IsHidden = true;
        subAdmin.SessionVersion++;
        await dbContext.SaveChangesAsync();
    }

    public async Task<SubAdminResponse> ReactivateAsync(Guid subAdminId)
    {
        var subAdmin = await FindSubAdmin(subAdminId);
        subAdmin.IsActive = true;
        await dbContext.SaveChangesAsync();
        return ToResponse(subAdmin, includeQrToken: true);
    }

    public async Task<SubAdminResponse> RegenerateQrAsync(Guid subAdminId)
    {
        var subAdmin = await FindSubAdmin(subAdminId);
        subAdmin.QrToken = GenerateQrToken();
        await dbContext.SaveChangesAsync();
        return ToResponse(subAdmin, includeQrToken: true);
    }

    public async Task<SubAdminResponse> SetPermissionsAsync(Guid subAdminId, UpdateSubAdminPermissionsRequest request)
    {
        var subAdmin = await FindSubAdmin(subAdminId);
        subAdmin.CanActivateMonthly = request.CanActivateMonthly;
        subAdmin.CanActivateAnnual = request.CanActivateAnnual;
        subAdmin.CanActivateCustomMonths = request.CanActivateCustomMonths;
        subAdmin.CanManageAccount = request.CanManageAccount;
        await dbContext.SaveChangesAsync();
        return ToResponse(subAdmin, includeQrToken: false);
    }

    private async Task<SubAdmin> FindSubAdmin(Guid subAdminId)
    {
        var subAdmin = await dbContext.SubAdmins.FirstOrDefaultAsync(s => s.Id == subAdminId);

        if (subAdmin is null)
        {
            throw new NotFoundException(nameof(SubAdmin), subAdminId);
        }

        return subAdmin;
    }

    private static string GenerateQrToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static SubAdminResponse ToResponse(SubAdmin subAdmin, bool includeQrToken) => new()
    {
        Id = subAdmin.Id,
        Name = subAdmin.Name,
        QrToken = includeQrToken ? subAdmin.QrToken : null,
        IsActive = subAdmin.IsActive,
        IsHidden = subAdmin.IsHidden,
        CanActivateMonthly = subAdmin.CanActivateMonthly,
        CanActivateAnnual = subAdmin.CanActivateAnnual,
        CanActivateCustomMonths = subAdmin.CanActivateCustomMonths,
        CanManageAccount = subAdmin.CanManageAccount,
        CreatedAt = subAdmin.CreatedAt
    };
}
