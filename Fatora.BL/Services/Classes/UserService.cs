using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fatora.BL.Services.Classes;

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

    public async Task<AdminUserResponse> UpdateSubscriptionAsync(Guid userId, UpdateSubscriptionRequest request)
    {
        var user = await FindUser(userId);

        var start = DateTime.UtcNow;
        user.SubscriptionType = request.SubscriptionType;
        user.SubscriptionStart = start;
        user.SubscriptionEnd = ComputeSubscriptionEnd(request.SubscriptionType, start);

        await dbContext.SaveChangesAsync();

        return ToAdminResponse(user);
    }

    public async Task<List<AdminUserResponse>> GetUsersAsync(string? search)
    {
        var query = dbContext.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u =>
                EF.Functions.ILike(u.UserName, $"%{search}%") ||
                EF.Functions.ILike(u.PhoneNumber, $"%{search}%") ||
                EF.Functions.ILike(u.Name, $"%{search}%"));
        }

        var users = await query.ToListAsync();

        return users.Select(ToAdminResponse).ToList();
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

    public async Task ResetPasswordAsync(Guid userId, string newPassword)
    {
        var user = await FindUser(userId);

        user.Password = passwordHasher.Hash(user, newPassword);

        var refreshTokens = await dbContext.RefreshTokens.Where(r => r.UserId == userId).ToListAsync();
        dbContext.RefreshTokens.RemoveRange(refreshTokens);

        await dbContext.SaveChangesAsync();
    }

    public async Task SuspendAsync(Guid userId)
    {
        var user = await FindUser(userId);

        user.IsActive = false;

        var refreshTokens = await dbContext.RefreshTokens.Where(r => r.UserId == userId).ToListAsync();
        dbContext.RefreshTokens.RemoveRange(refreshTokens);

        await dbContext.SaveChangesAsync();
    }

    public async Task ActivateAsync(Guid userId)
    {
        var user = await FindUser(userId);

        user.IsActive = true;
        await dbContext.SaveChangesAsync();
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

    internal static DateTime? ComputeSubscriptionEnd(SubscriptionType type, DateTime start) => type switch
    {
        SubscriptionType.Trial => start.AddDays(3),
        SubscriptionType.Monthly => start.AddDays(30),
        SubscriptionType.Annual => start.AddDays(365),
        SubscriptionType.Lifetime => null,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static AdminUserResponse ToAdminResponse(User user) => new()
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
        BankName = user.BankName,
        AccountNumber = user.AccountNumber,
        IBAN = user.IBAN,
        SubscriptionType = user.SubscriptionType.ToString(),
        SubscriptionStart = user.SubscriptionStart,
        SubscriptionEnd = user.SubscriptionEnd,
        AccountStatus = ComputeAccountStatus(user)
    };
}
