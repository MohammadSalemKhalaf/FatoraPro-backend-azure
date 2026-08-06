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

        if (storedToken.ExpiresOnUtc < DateTime.UtcNow || !storedToken.Rep.IsActive)
        {
            throw new UnauthorizedException("Invalid or expired refresh token");
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

    private async Task<JwtTokenResponse> GenerateTokenAsync(Rep rep)
    {
        var jwtSettings = configuration.GetSection("JwtSettings");

        var issuer = jwtSettings["Issuer"];
        var audience = jwtSettings["Audience"];
        var key = jwtSettings["SecretKey"];
        var expiry = DateTime.UtcNow.AddMinutes(int.Parse(jwtSettings["TokenExpirationInMinutes"]!));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, rep.Id.ToString()),
            new(ClaimTypes.Role, "Rep"),
            new("ownerId", rep.OwnerUserId.ToString()),
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiry,
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key!)),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var securityToken = tokenHandler.CreateToken(descriptor);

        var rawRefreshToken = await IssueRefreshTokenAsync(rep);

        return new JwtTokenResponse
        {
            AccessToken = tokenHandler.WriteToken(securityToken),
            RefreshToken = rawRefreshToken,
            Expires = expiry,
            Name = rep.Name
        };
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
