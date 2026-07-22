using System.Security.Cryptography;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Fatora.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Fatora.BL.Services.Classes;

public class AdminRecoveryService(
    AppDbContext dbContext,
    IPasswordHasherService passwordHasher,
    IEmailService emailService,
    IConfiguration configuration) : IAdminRecoveryService
{
    private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(10);

    public async Task RequestPasswordResetAsync(string userName)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.UserName == userName && u.Role == Role.Admin);

        // Silent no-op for a non-existent or non-Admin username - the response is identical either
        // way, so this can't be used to discover which usernames exist or which ones are Admin.
        if (user is null)
        {
            return;
        }

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        dbContext.PasswordResetOtps.Add(new PasswordResetOtp
        {
            UserId = user.Id,
            Code = code,
            ExpiresOnUtc = DateTime.UtcNow.Add(OtpLifetime),
            Used = false
        });

        await dbContext.SaveChangesAsync();

        var recoveryEmail = configuration["AdminRecovery:Email"]!;
        await emailService.SendAsync(
            recoveryEmail,
            "Fatora Admin Password Reset Code",
            $"Your password reset code is: {code}\nThis code expires in 10 minutes and can only be used once.");
    }

    public async Task ResetPasswordWithOtpAsync(string userName, string otp, string newPassword)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.UserName == userName && u.Role == Role.Admin);

        // Same message for "no such admin" and "wrong/expired code" - avoids confirming which
        // usernames are valid Admin accounts.
        if (user is null)
        {
            throw new UnauthorizedException("Invalid or expired code.");
        }

        var resetOtp = await dbContext.PasswordResetOtps
            .Where(o => o.UserId == user.Id && o.Code == otp && !o.Used && o.ExpiresOnUtc > DateTime.UtcNow)
            .OrderByDescending(o => o.Id)
            .FirstOrDefaultAsync();

        if (resetOtp is null)
        {
            throw new UnauthorizedException("Invalid or expired code.");
        }

        resetOtp.Used = true;
        user.Password = passwordHasher.Hash(user, newPassword);

        var refreshTokens = await dbContext.RefreshTokens.Where(r => r.UserId == user.Id).ToListAsync();
        dbContext.RefreshTokens.RemoveRange(refreshTokens);

        await dbContext.SaveChangesAsync();
    }
}
