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

// Parallel to RepAuthService/JwtTokenProviderService - reuses the exact same
// JwtSettings (key/issuer/audience) so the existing [Authorize] pipeline
// needs no changes; only the claims differ. Unlike RepAuthService, there's
// no owning tenant's subscription status to check here - a SubAdmin belongs
// to the platform directly (see SubAdmin.cs), so its own IsActive/
// SessionVersion are the only gates, and there's no ownerId claim or
// pending-sync handoff to build (nothing offline to hand over - every
// SubAdmin action is a plain online API call).
public class SubAdminAuthService(IConfiguration configuration, AppDbContext dbContext) : ISubAdminAuthService
{
    public async Task<JwtTokenResponse> LoginByQrAsync(string qrToken)
    {
        var subAdmin = await dbContext.SubAdmins.FirstOrDefaultAsync(s => s.QrToken == qrToken);

        if (subAdmin is null || !subAdmin.IsActive)
        {
            throw new UnauthorizedException("Invalid or inactive QR code.");
        }

        return await GenerateTokenAsync(subAdmin);
    }

    public async Task<JwtTokenResponse?> TryRefreshAsync(string refreshToken)
    {
        var hashedToken = HashToken(refreshToken);
        var storedToken = await dbContext.SubAdminRefreshTokens
            .Include(r => r.SubAdmin)
            .FirstOrDefaultAsync(r => r.Token == hashedToken);

        if (storedToken is null)
        {
            return null;
        }

        if (storedToken.ExpiresOnUtc < DateTime.UtcNow)
        {
            throw new UnauthorizedException("Invalid or expired refresh token", AccountStatusErrorCodes.SessionExpired);
        }

        // IsActive alone isn't enough: a plain logout never sets it false,
        // only bumps SessionVersion - checking both catches that case too,
        // same reasoning as RepAuthService's equivalent check.
        if (!storedToken.SubAdmin.IsActive ||
            storedToken.IssuedAtSessionVersion != storedToken.SubAdmin.SessionVersion)
        {
            throw new UnauthorizedException(
                "This session has ended.",
                AccountStatusErrorCodes.SubAdminSessionEnded);
        }

        // A superseded token gets exactly one more chance to reissue, within
        // JwtTokenProviderService.RefreshRotationGrace - see that field's
        // comment for why. Checked last, after IsActive/SessionVersion, so a
        // token that happens to fall in its grace window still correctly
        // rejects if the Admin ended this session in the meantime.
        if (storedToken.ReplacedAtUtc is { } replacedAt &&
            replacedAt < DateTime.UtcNow - JwtTokenProviderService.RefreshRotationGrace)
        {
            throw new UnauthorizedException("Invalid or expired refresh token", AccountStatusErrorCodes.SessionExpired);
        }

        return await GenerateTokenAsync(storedToken.SubAdmin);
    }

    private async Task<JwtTokenResponse> GenerateTokenAsync(SubAdmin subAdmin)
    {
        var expiry = DateTime.UtcNow.AddMinutes(
            int.Parse(configuration.GetSection("JwtSettings")["TokenExpirationInMinutes"]!));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subAdmin.Id.ToString()),
            new(ClaimTypes.Role, "SubAdmin"),
            new("sessionVersion", subAdmin.SessionVersion.ToString()),
        };

        var accessToken = WriteToken(claims, expiry);
        var rawRefreshToken = await IssueRefreshTokenAsync(subAdmin);

        return new JwtTokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = rawRefreshToken,
            Expires = expiry,
            Name = subAdmin.Name
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

    private async Task<string> IssueRefreshTokenAsync(SubAdmin subAdmin)
    {
        var existingTokens = await dbContext.SubAdminRefreshTokens.Where(r => r.SubAdminId == subAdmin.Id).ToListAsync();
        var now = DateTime.UtcNow;

        // See JwtTokenProviderService.IssueRefreshTokenAsync - same
        // grace-window-then-cleanup shape, mirrored here for SubAdmin sessions.
        foreach (var existing in existingTokens)
        {
            if (existing.ReplacedAtUtc is { } replacedAt &&
                replacedAt < now - JwtTokenProviderService.RefreshRotationGrace)
            {
                dbContext.SubAdminRefreshTokens.Remove(existing);
            }
            else
            {
                // ??=, not = - see JwtTokenProviderService.IssueRefreshTokenAsync's
                // identical comment. A plain assignment here would let
                // unrelated activity on this SubAdmin's session keep
                // resetting an old row's clock instead of it expiring 90s
                // after its own supersession.
                existing.ReplacedAtUtc ??= now;
            }
        }

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var refreshToken = new SubAdminRefreshToken
        {
            Token = HashToken(rawToken),
            SubAdminId = subAdmin.Id,
            ExpiresOnUtc = now.AddDays(7),
            IssuedAtSessionVersion = subAdmin.SessionVersion
        };

        await dbContext.SubAdminRefreshTokens.AddAsync(refreshToken);
        await dbContext.SaveChangesAsync();

        return rawToken;
    }

    private static string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
