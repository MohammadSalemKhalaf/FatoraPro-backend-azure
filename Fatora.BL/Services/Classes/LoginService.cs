using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fatora.BL.Services.Classes;

public class LoginService(
    AppDbContext dbContext,
    IPasswordHasherService passwordHasher,
    IJwtTokenProviderService jwtTokenProvider,
    ILogger<LoginService> logger) : ILoginService
{
    public async Task<JwtTokenResponse> Login(LoginRequest request)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.UserName.Equals(request.UserName));

        // Deliberately the same exception/message for "user not found" and "wrong password" -
        // distinguishing them would let a caller enumerate valid usernames.
        if (user is null || !passwordHasher.Verify(user, request.Password, user.Password))
        {
            throw new UnauthorizedException("Invalid username or password");
        }

        // Safe to be specific here - the caller already proved they know valid credentials,
        // so this doesn't create an enumeration risk like the check above does.
        var status = UserService.ComputeAccountStatus(user);
        if (status is "Expired" or "Suspended")
        {
            throw new UnauthorizedException($"This account is {status.ToLowerInvariant()}.", AccountStatusErrorCodes.For(status));
        }

        // Bumping SessionVersion here, before generating the new token,
        // means the new token embeds the incremented value while any
        // token already issued to a previous device still carries the OLD
        // one - that device gets rejected as soon as it makes its next
        // request (AccountStatusFilter) or tries to refresh
        // (JwtTokenProviderService.RefreshTokenAsync), enforcing "one
        // signed-in device at a time" for this account. Harmless on a
        // first-ever login (0 -> 1): there's no older token to invalidate.
        var previousSessionVersion = user.SessionVersion;
        user.SessionVersion++;
        await dbContext.SaveChangesAsync();

        logger.LogInformation(
            "Session revoked. AccountId: {AccountId}, RevokedSessionVersion: {RevokedSessionVersion}, NewSessionVersion: {NewSessionVersion}",
            user.Id,
            previousSessionVersion,
            user.SessionVersion);

        return await jwtTokenProvider.GenerateToken(user);
    }
}
