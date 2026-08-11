using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Fatora.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fatora.BL.Services.Classes;

// Machine-readable codes for the two ComputeAccountStatus outcomes that block access - shared by
// every place that throws for them (LoginService, AccountStatusFilter, JwtTokenProviderService) so
// the frontend can react precisely instead of string-matching the human-readable message.
public static class AccountStatusErrorCodes
{
    public const string TrialExpired = "TRIAL_EXPIRED";
    public const string AccountSuspended = "ACCOUNT_SUSPENDED";

    // Distinct from the two above: those describe the owning business's own
    // subscription state (still meaningful for a Rep working under it - see
    // AccountStatusFilter). This one is Rep-specific - the owner logged this
    // one sub-account out or deactivated it, nothing to do with billing.
    public const string RepSessionEnded = "REP_SESSION_ENDED";

    // SubAdmin-specific counterpart to RepSessionEnded - the Admin logged
    // this sub-account out or deactivated it, nothing to do with billing (a
    // SubAdmin has no subscription of its own to expire - see SubAdmin.cs).
    public const string SubAdminSessionEnded = "SUBADMIN_SESSION_ENDED";

    public static string For(string accountStatus) =>
        accountStatus == "Suspended" ? AccountSuspended : TrialExpired;
}

public class UserService(AppDbContext dbContext, IPasswordHasherService passwordHasher) : IUserService
{
    public async Task<UserResponse> CreateSalesRepAsync(CreateSalesRepRequest request)
    {
        var usernameTaken = await dbContext.Users.AnyAsync(u => u.UserName == request.UserName);

        if (usernameTaken)
        {
            throw new ConflictException(nameof(User), request.UserName);
        }

        var start = DateTime.UtcNow;
        var user = new User
        {
            UserName = request.UserName,
            Password = request.Password,
            Name = request.Name,
            PhoneNumber = request.PhoneNumber,
            BusinessName = request.BusinessName,
            City = request.City,
            Street = request.Street,
            Role = Role.SalesRep,
            SubscriptionType = SubscriptionType.Trial,
            SubscriptionStart = start,
            SubscriptionEnd = ComputeSubscriptionEnd(SubscriptionType.Trial, start),
            CreatedAt = start
        };

        user.Password = passwordHasher.Hash(user, user.Password);

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        return ToResponse(user);
    }

    public async Task<UserResponse> RegisterAsync(RegisterRequest request)
    {
        var usernameTaken = await dbContext.Users.AnyAsync(u => u.UserName == request.UserName);

        if (usernameTaken)
        {
            throw new ConflictException(nameof(User), request.UserName);
        }

        var start = DateTime.UtcNow;
        var user = new User
        {
            UserName = request.UserName,
            Password = request.Password,
            Name = request.Name,
            PhoneNumber = request.PhoneNumber,
            BusinessName = request.BusinessName,
            Role = Role.SalesRep,
            SubscriptionType = SubscriptionType.Trial,
            SubscriptionStart = start,
            SubscriptionEnd = ComputeSubscriptionEnd(SubscriptionType.Trial, start),
            CreatedAt = start
        };

        user.Password = passwordHasher.Hash(user, user.Password);

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        return ToResponse(user);
    }

    public async Task<AdminUserResponse> UpdateSubscriptionAsync(Guid userId, UpdateSubscriptionRequest request, Guid? actingSubAdminId = null)
    {
        if (actingSubAdminId is not null)
        {
            await EnsureSubAdminCanActivateAsync(actingSubAdminId.Value, request.SubscriptionType);
        }

        var user = await FindUser(userId);

        var start = DateTime.UtcNow;
        user.SubscriptionType = request.SubscriptionType;
        user.SubscriptionStart = start;
        user.SubscriptionEnd = ComputeSubscriptionEnd(request.SubscriptionType, start, request.CustomMonths);

        // Only ever meaningful for Custom - clearing it on every other type
        // means a later read never sees a stale month count left over from
        // a previous Custom activation.
        user.CustomMonths = request.SubscriptionType == SubscriptionType.Custom ? request.CustomMonths : null;

        // One row per activation, regardless of who performed it - see
        // SubscriptionActivation.cs for why (UpdateSubscriptionAsync itself
        // has no other history).
        dbContext.SubscriptionActivations.Add(new SubscriptionActivation
        {
            UserId = user.Id,
            SubscriptionType = request.SubscriptionType,
            CustomMonths = user.CustomMonths,
            PerformedBySubAdminId = actingSubAdminId,
            ActivatedAt = start,
            Latitude = request.Latitude,
            Longitude = request.Longitude
        });

        await dbContext.SaveChangesAsync();

        return ToAdminResponse(user);
    }

    // A SubAdmin can only ever activate the durations it was explicitly
    // granted (see SubAdmin.CanActivateMonthly/Annual/CustomMonths) - Trial
    // and Lifetime are never grantable to a SubAdmin at all, regardless of
    // its flags, since neither is "a duration you activate for a paying
    // subscriber" in the permission's sense. The top Admin (actingSubAdminId
    // null) always bypasses this entirely - see UpdateSubscriptionAsync.
    private async Task EnsureSubAdminCanActivateAsync(Guid subAdminId, SubscriptionType requestedType)
    {
        var subAdmin = await dbContext.SubAdmins.FirstOrDefaultAsync(s => s.Id == subAdminId);
        if (subAdmin is null)
        {
            throw new NotFoundException(nameof(SubAdmin), subAdminId);
        }

        var allowed = requestedType switch
        {
            SubscriptionType.Monthly => subAdmin.CanActivateMonthly,
            SubscriptionType.Annual => subAdmin.CanActivateAnnual,
            SubscriptionType.Custom => subAdmin.CanActivateCustomMonths,
            _ => false
        };

        if (!allowed)
        {
            throw new ForbiddenException($"You are not permitted to activate a {requestedType} subscription.");
        }
    }

    public async Task<List<AdminUserResponse>> GetUsersAsync(string? search, Guid? subAdminIdFilter = null, Guid? actingSubAdminId = null)
    {
        var query = dbContext.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u =>
                EF.Functions.ILike(u.UserName, $"%{search}%") ||
                EF.Functions.ILike(u.PhoneNumber, $"%{search}%") ||
                EF.Functions.ILike(u.Name, $"%{search}%"));
        }

        // A SubAdmin session's own id always wins over whatever it passed as
        // subAdminIdFilter - see IUserService.GetUsersAsync.
        var effectiveSubAdminFilter = actingSubAdminId ?? subAdminIdFilter;
        if (effectiveSubAdminFilter is not null)
        {
            query = query.Where(u => u.ManagedBySubAdminId == effectiveSubAdminFilter);
        }

        var users = await query.ToListAsync();

        // One batch lookup for every SubAdmin name this page of results
        // needs, instead of a per-row query - see the plan's own note on
        // this.
        var subAdminIds = users.Where(u => u.ManagedBySubAdminId is not null)
            .Select(u => u.ManagedBySubAdminId!.Value)
            .Distinct()
            .ToList();
        var subAdminNames = subAdminIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.SubAdmins
                .Where(s => subAdminIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name);

        return users.Select(u => ToAdminResponse(u, subAdminNames)).ToList();
    }

    public async Task ClaimSubscriberAsync(Guid subscriberId, Guid? actingSubAdminId)
    {
        var user = await FindUser(subscriberId);

        // null (top Admin) clears attribution; a SubAdmin's own id claims it
        // - no branching needed, this is exactly the confirmed semantics.
        user.ManagedBySubAdminId = actingSubAdminId;

        await dbContext.SaveChangesAsync();
    }

    public async Task<UserResponse> GetProfileAsync(Guid userId)
    {
        var user = await FindUser(userId);
        return ToResponse(user);
    }

    public async Task<string?> GetLogoUrlAsync(Guid userId)
    {
        var user = await FindUser(userId);
        return user.LogoUrl;
    }

    public async Task<UserResponse> UpdateLogoAsync(Guid userId, string logoUrl)
    {
        var user = await FindUser(userId);

        user.LogoUrl = logoUrl;
        await dbContext.SaveChangesAsync();

        return ToResponse(user);
    }

    public async Task<UserResponse> DeleteLogoAsync(Guid userId)
    {
        var user = await FindUser(userId);

        user.LogoUrl = null;
        await dbContext.SaveChangesAsync();

        return ToResponse(user);
    }

    public async Task<UserResponse> UpdateBankDetailsAsync(Guid userId, UpdateBankDetailsRequest request)
    {
        var user = await FindUser(userId);

        user.BankName = request.BankName;
        user.AccountNumber = request.AccountNumber;
        user.IBAN = request.IBAN;

        await dbContext.SaveChangesAsync();

        return ToResponse(user);
    }

    public async Task<UserResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request)
    {
        var user = await FindUser(userId);

        user.Name = request.Name;
        user.PhoneNumber = request.PhoneNumber;
        user.BusinessName = request.BusinessName;
        user.Street = request.Street;
        user.City = request.City;

        await dbContext.SaveChangesAsync();

        return ToResponse(user);
    }

    public async Task DeleteAccountAsync(Guid userId, string password)
    {
        var user = await FindUser(userId);

        if (!passwordHasher.Verify(user, password, user.Password))
        {
            throw new UnauthorizedException("Incorrect password.");
        }

        dbContext.Users.Remove(user);
        await dbContext.SaveChangesAsync();
    }

    public async Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword)
    {
        var user = await FindUser(userId);

        if (!passwordHasher.Verify(user, currentPassword, user.Password))
        {
            throw new UnauthorizedException("Current password is incorrect.");
        }

        user.Password = passwordHasher.Hash(user, newPassword);

        var refreshTokens = await dbContext.RefreshTokens.Where(r => r.UserId == userId).ToListAsync();
        dbContext.RefreshTokens.RemoveRange(refreshTokens);

        await dbContext.SaveChangesAsync();
    }

    public async Task ResetPasswordAsync(Guid userId, string newPassword, Guid? actingSubAdminId = null)
    {
        if (actingSubAdminId is not null)
        {
            await EnsureSubAdminCanResetPasswordAsync(actingSubAdminId.Value);
        }

        var user = await FindUser(userId);

        user.Password = passwordHasher.Hash(user, newPassword);

        var refreshTokens = await dbContext.RefreshTokens.Where(r => r.UserId == userId).ToListAsync();
        dbContext.RefreshTokens.RemoveRange(refreshTokens);

        await dbContext.SaveChangesAsync();
    }

    public async Task SuspendAsync(Guid userId, Guid? actingSubAdminId = null, LocationRequest? location = null)
    {
        if (actingSubAdminId is not null)
        {
            await EnsureSubAdminCanManageAccountAsync(actingSubAdminId.Value);
        }

        var user = await FindUser(userId);

        user.IsActive = false;

        var refreshTokens = await dbContext.RefreshTokens.Where(r => r.UserId == userId).ToListAsync();
        dbContext.RefreshTokens.RemoveRange(refreshTokens);

        dbContext.SubAdminAccountActions.Add(new SubAdminAccountAction
        {
            UserId = user.Id,
            ActionType = SubAdminAccountActionType.Suspended,
            PerformedBySubAdminId = actingSubAdminId,
            CreatedAt = DateTime.UtcNow,
            Latitude = location?.Latitude,
            Longitude = location?.Longitude
        });

        await dbContext.SaveChangesAsync();
    }

    public async Task ActivateAsync(Guid userId, Guid? actingSubAdminId = null, LocationRequest? location = null)
    {
        if (actingSubAdminId is not null)
        {
            await EnsureSubAdminCanManageAccountAsync(actingSubAdminId.Value);
        }

        var user = await FindUser(userId);

        user.IsActive = true;

        dbContext.SubAdminAccountActions.Add(new SubAdminAccountAction
        {
            UserId = user.Id,
            ActionType = SubAdminAccountActionType.Reactivated,
            PerformedBySubAdminId = actingSubAdminId,
            CreatedAt = DateTime.UtcNow,
            Latitude = location?.Latitude,
            Longitude = location?.Longitude
        });

        await dbContext.SaveChangesAsync();
    }

    // Mirrors EnsureSubAdminCanActivateAsync's shape. Gates Suspend/Activate
    // only - see SubAdmin.CanManageAccount, independent of the password-reset
    // permission below. The top Admin (actingSubAdminId null) always bypasses
    // this.
    private async Task EnsureSubAdminCanManageAccountAsync(Guid subAdminId)
    {
        var subAdmin = await dbContext.SubAdmins.FirstOrDefaultAsync(s => s.Id == subAdminId);
        if (subAdmin is null)
        {
            throw new NotFoundException(nameof(SubAdmin), subAdminId);
        }

        if (!subAdmin.CanManageAccount)
        {
            throw new ForbiddenException("You are not permitted to manage this subscriber's account.");
        }
    }

    // Gates ResetPassword only - see SubAdmin.CanResetPassword, independent
    // of EnsureSubAdminCanManageAccountAsync above, so the top Admin can
    // grant one without the other. The top Admin (actingSubAdminId null)
    // always bypasses this.
    private async Task EnsureSubAdminCanResetPasswordAsync(Guid subAdminId)
    {
        var subAdmin = await dbContext.SubAdmins.FirstOrDefaultAsync(s => s.Id == subAdminId);
        if (subAdmin is null)
        {
            throw new NotFoundException(nameof(SubAdmin), subAdminId);
        }

        if (!subAdmin.CanResetPassword)
        {
            throw new ForbiddenException("You are not permitted to reset this subscriber's password.");
        }
    }

    // A one-way switch for now - see User.IsSalesManager. Idempotent:
    // calling it again on an account that's already a manager is a no-op,
    // not an error.
    public async Task<UserResponse> EnableSalesManagerAsync(Guid userId)
    {
        var user = await FindUser(userId);

        user.IsSalesManager = true;
        await dbContext.SaveChangesAsync();

        return ToResponse(user);
    }

    private async Task<User> FindUser(Guid userId)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null)
        {
            throw new NotFoundException(nameof(User), userId);
        }

        return user;
    }

    public static string ComputeAccountStatus(User user)
    {
        if (!user.IsActive)
        {
            return "Suspended";
        }

        if (user.SubscriptionType == SubscriptionType.Lifetime)
        {
            return "Active";
        }

        if (user.SubscriptionEnd is not null && user.SubscriptionEnd < DateTime.UtcNow)
        {
            return "Expired";
        }

        return user.SubscriptionType == SubscriptionType.Trial ? "Trial" : "Active";
    }

    internal static DateTime? ComputeSubscriptionEnd(SubscriptionType type, DateTime start, int? customMonths = null) => type switch
    {
        SubscriptionType.Trial => start.AddDays(3),
        SubscriptionType.Monthly => start.AddDays(30),
        SubscriptionType.Annual => start.AddDays(365),
        SubscriptionType.Lifetime => null,
        SubscriptionType.Custom => start.AddMonths(customMonths ?? throw new ArgumentNullException(nameof(customMonths), "CustomMonths is required when SubscriptionType is Custom")),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    // subAdminNames: the batch lookup GetUsersAsync builds once for a whole
    // page of results - omitted (null) at the single-user call site in
    // UpdateSubscriptionAsync, where a full-dictionary batch would be
    // overkill for one row; ManagedBySubAdminName is simply left
    // unpopulated there since that response isn't used to display
    // attribution.
    private static AdminUserResponse ToAdminResponse(User user, IReadOnlyDictionary<Guid, string>? subAdminNames = null) => new()
    {
        Id = user.Id,
        UserName = user.UserName,
        Name = user.Name,
        PhoneNumber = user.PhoneNumber,
        City = user.City,
        BusinessName = user.BusinessName,
        Role = user.Role.ToString(),
        IsActive = user.IsActive,
        SubscriptionType = user.SubscriptionType.ToString(),
        SubscriptionStart = user.SubscriptionStart,
        SubscriptionEnd = user.SubscriptionEnd,
        CustomMonths = user.CustomMonths,
        ManagedBySubAdminId = user.ManagedBySubAdminId,
        ManagedBySubAdminName = user.ManagedBySubAdminId is not null && subAdminNames is not null
            ? subAdminNames.GetValueOrDefault(user.ManagedBySubAdminId.Value)
            : null,
        AccountStatus = ComputeAccountStatus(user),
        CreatedAt = user.CreatedAt
    };

    private static UserResponse ToResponse(User user) => new()
    {
        Id = user.Id,
        UserName = user.UserName,
        Name = user.Name,
        PhoneNumber = user.PhoneNumber,
        BusinessName = user.BusinessName,
        LogoUrl = user.LogoUrl,
        City = user.City,
        Street = user.Street,
        Role = user.Role.ToString(),
        IsActive = user.IsActive,
        IsSalesManager = user.IsSalesManager,
        BankName = user.BankName,
        AccountNumber = user.AccountNumber,
        IBAN = user.IBAN,
        SubscriptionType = user.SubscriptionType.ToString(),
        SubscriptionStart = user.SubscriptionStart,
        SubscriptionEnd = user.SubscriptionEnd,
        AccountStatus = ComputeAccountStatus(user)
    };
}
