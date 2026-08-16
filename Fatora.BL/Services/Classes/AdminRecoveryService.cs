using System.Security.Cryptography;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Fatora.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Fatora.BL.Services.Classes;

public class AdminRecoveryService(
    AppDbContext dbContext,
    IPasswordHasherService passwordHasher,
    IEmailService emailService,
    IConfiguration configuration,
    ILogger<AdminRecoveryService> logger) : IAdminRecoveryService
{
    private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(10);

    // A 6-digit code is only defensible because guessing is bounded on both
    // axes: at most one code is ever live for a user (older ones are retired
    // the moment a new one is issued, so N codes can never share the 9x10^5
    // keyspace - GetInt32(100000, 1000000) never emits a leading zero - and
    // collapse it to ~9x10^5/N), and each one dies after this many wrong
    // guesses. Re-opening the window costs an attacker a fresh
    // forgot-password call, which is rate-limited at the endpoint.
    private const int MaxVerificationAttempts = 5;

    public async Task RequestPasswordResetAsync(string userName)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.UserName == userName && u.Role == Role.Admin);

        // Silent no-op for a non-existent or non-Admin username - the response is identical either
        // way, so this can't be used to discover which usernames exist or which ones are Admin.
        if (user is null)
        {
            return;
        }

        // Retire anything still outstanding before issuing. Without this, every
        // call stacked another independently-valid code and the verification
        // query below would match any one of them - so flooding this endpoint
        // shrank the search space by exactly the number of calls made.
        var outstanding = await dbContext.PasswordResetOtps
            .Where(o => o.UserId == user.Id && !o.Used)
            .ToListAsync();

        foreach (var previous in outstanding)
        {
            previous.Used = true;
        }

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        dbContext.PasswordResetOtps.Add(new PasswordResetOtp
        {
            UserId = user.Id,
            CodeHash = HashCode(code),
            ExpiresOnUtc = DateTime.UtcNow.Add(OtpLifetime),
            Used = false
        });

        await dbContext.SaveChangesAsync();

        // Fired, not awaited. The code already exists and is already valid
        // the moment the save above completes - the caller's request is
        // meaningfully done at that point, regardless of how long Gmail's
        // relay takes to accept the message. Awaiting it here tied the HTTP
        // response, and so the app's loading spinner, to that delivery time,
        // which was observed hanging for minutes. Best-effort: EmailService
        // now carries its own timeout, and a delivery failure has nothing
        // useful to recover into here anyway - the response already reads
        // "sent" either way, since that's what stays true from the outside.
        var recoveryEmail = configuration["AdminRecovery:Email"]!;
        _ = SendResetCodeEmailAsync(recoveryEmail, code);
    }

    private async Task SendResetCodeEmailAsync(string recoveryEmail, string code)
    {
        try
        {
            await emailService.SendAsync(
                recoveryEmail,
                "Fatora Admin Password Reset Code",
                $"Your password reset code is: {code}\nThis code expires in 10 minutes and can only be used once.");
        }
        catch (Exception ex)
        {
            // Not rethrown - see the comment at the call site for why the
            // caller's request must not fail here. Still logged: a swallowed
            // exception with no trace at all is exactly what let this OTP
            // silently stop arriving (an expired SMTP credential, a blocked
            // outbound port, anything) go completely unnoticed until a real
            // user reported it.
            logger.LogError(ex, "Failed to send admin password reset code email to {RecoveryEmail}",
                recoveryEmail);
        }
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

        // Fetched by owner rather than by code, because the code is stored
        // hashed and the comparison has to happen in memory - which is also
        // what makes it possible to count a wrong guess against the row.
        // RequestPasswordResetAsync guarantees at most one is outstanding.
        var resetOtp = await dbContext.PasswordResetOtps
            .Where(o => o.UserId == user.Id && !o.Used && o.ExpiresOnUtc > DateTime.UtcNow)
            .OrderByDescending(o => o.Id)
            .FirstOrDefaultAsync();

        if (resetOtp is null)
        {
            throw new UnauthorizedException("Invalid or expired code.");
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(HashCode(otp)),
                Convert.FromHexString(resetOtp.CodeHash)))
        {
            resetOtp.AttemptCount++;

            // Burn the code rather than merely counting - leaving it alive
            // would let the attacker keep guessing against the same one.
            if (resetOtp.AttemptCount >= MaxVerificationAttempts)
            {
                resetOtp.Used = true;
            }

            await dbContext.SaveChangesAsync();
            throw new UnauthorizedException("Invalid or expired code.");
        }

        resetOtp.Used = true;
        user.Password = passwordHasher.Hash(user, newPassword);

        var refreshTokens = await dbContext.RefreshTokens.Where(r => r.UserId == user.Id).ToListAsync();
        dbContext.RefreshTokens.RemoveRange(refreshTokens);

        await dbContext.SaveChangesAsync();
    }

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code)));
}
