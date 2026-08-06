using Fatora.BL.DTOs.Responses;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Fatora.BL.Services.Classes;

// Parallel to JwtTokenProviderService rather than a retrofit of it - see
// Rep.cs/RepRefreshToken.cs for why. Reuses the exact same JwtSettings
// (key/issuer/audience) so the existing [Authorize] pipeline needs no
// changes at all; only the claims differ.
public class RepAuthService(IConfiguration configuration, AppDbContext dbContext) : IRepAuthService
{
    public async Task<JwtTokenResponse> LoginByQrAsync(string qrToken)
    {
        var rep = await dbContext.Reps
            .Include(r => r.OwnerUser)
            .FirstOrDefaultAsync(r => r.QrToken == qrToken);

        if (rep is null || !rep.IsActive)
        {
            throw new UnauthorizedException("Invalid or inactive QR code.");
        }

        var ownerStatus = UserService.ComputeAccountStatus(rep.OwnerUser);
        if (ownerStatus is "Expired" or "Suspended")
        {
            throw new UnauthorizedException(
                $"This account is {ownerStatus.ToLowerInvariant()}.",
                AccountStatusErrorCodes.For(ownerStatus));
        }

        return await GenerateTokenAsync(rep);
    }

    public async Task<JwtTokenResponse?> TryRefreshAsync(string refreshToken)
    {
        var hashedToken = HashToken(refreshToken);
        var storedToken = await dbContext.RepRefreshTokens
            .Include(r => r.Rep)
            .ThenInclude(r => r.OwnerUser)
            .FirstOrDefaultAsync(r => r.Token == hashedToken);

        if (storedToken is null)
        {
            return null;
        }

        if (storedToken.ExpiresOnUtc < DateTime.UtcNow)
        {
            throw new UnauthorizedException("Invalid or expired refresh token");
        }

        // Distinct from a plain expiry: this token is still technically
        // live, but the owner deactivated the rep since it was issued - the
        // dedicated code lets the frontend show "your session ended, re-scan
        // your QR" instead of a silent, unexplained logout (see
        // AccountStatusFilter for the equivalent mid-session check, which
        // only fires while the *access* token is still valid - this branch
        // is what catches the case where it had already expired by the
        // time the device reconnects and tries to refresh).
        if (!storedToken.Rep.IsActive)
        {
            // Carried alongside the rejection, not instead of it - the
            // device still needs to land on "your session ended" either
            // way. If it also has unsynced offline work, this narrow,
            // short-lived token is its one remaining chance to hand that
            // over for the owner to review before it's fully locked out.
            throw new UnauthorizedException(
                "This rep session has ended.",
                AccountStatusErrorCodes.RepSessionEnded,
                new Dictionary<string, object> { ["pendingSyncToken"] = GeneratePendingSyncToken(storedToken.Rep) });
        }

        var ownerStatus = UserService.ComputeAccountStatus(storedToken.Rep.OwnerUser);
        if (ownerStatus is "Expired" or "Suspended")
        {
            throw new UnauthorizedException(
                $"This account is {ownerStatus.ToLowerInvariant()}.",
                AccountStatusErrorCodes.For(ownerStatus));
        }

        return await GenerateTokenAsync(storedToken.Rep);
    }

    public async Task RevokeSessionAsync(Guid repId)
    {
        var tokens = await dbContext.RepRefreshTokens.Where(r => r.RepId == repId).ToListAsync();
        dbContext.RepRefreshTokens.RemoveRange(tokens);
        await dbContext.SaveChangesAsync();
    }

    public string GeneratePendingSyncToken(Rep rep)
    {
        // Deliberately short - just long enough for a device that's already
        // sitting on the response to make its one upload call, not a normal
        // session lifetime. No sessionVersion/ownerId claim like a real Rep
        // token carries: this role is authorized for exactly one endpoint
        // (see AccountStatusFilter), which only needs to know whose batch
        // this is.
        var expiry = DateTime.UtcNow.AddMinutes(10);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, rep.Id.ToString()),
            new(ClaimTypes.Role, "RepPendingSync"),
        };
        return WriteToken(claims, expiry);
    }

    private async Task<JwtTokenResponse> GenerateTokenAsync(Rep rep)
    {
        var expiry = DateTime.UtcNow.AddMinutes(
            int.Parse(configuration.GetSection("JwtSettings")["TokenExpirationInMinutes"]!));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, rep.Id.ToString()),
            new(ClaimTypes.Role, "Rep"),
            new("ownerId", rep.OwnerUserId.ToString()),
            new("sessionVersion", rep.SessionVersion.ToString()),
        };

        var accessToken = WriteToken(claims, expiry);
        var rawRefreshToken = await IssueRefreshTokenAsync(rep);

        return new JwtTokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = rawRefreshToken,
            Expires = expiry,
            Name = rep.Name
        };
    }

    private string WriteToken(List<Claim> claims, DateTime expiry)
    {
        var jwtSettings = configuration.GetSection("JwtSettings");
        var key = jwtSettings["SecretKey"];

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiry,
            Issuer = jwtSettings["Issuer"],
            Audience = jwtSettings["Audience"],
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key!)),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        return tokenHandler.WriteToken(tokenHandler.CreateToken(descriptor));
    }

    private async Task<string> IssueRefreshTokenAsync(Rep rep)
    {
        var existingTokens = await dbContext.RepRefreshTokens.Where(r => r.RepId == rep.Id).ToListAsync();
        dbContext.RepRefreshTokens.RemoveRange(existingTokens);

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var refreshToken = new RepRefreshToken
        {
            Token = HashToken(rawToken),
            RepId = rep.Id,
            ExpiresOnUtc = DateTime.UtcNow.AddDays(7)
        };

        await dbContext.RepRefreshTokens.AddAsync(refreshToken);
        await dbContext.SaveChangesAsync();

        return rawToken;
    }

    private static string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
