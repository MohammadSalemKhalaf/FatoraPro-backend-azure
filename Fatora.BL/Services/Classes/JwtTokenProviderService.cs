using Fatora.BL.DTOs.Responses;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Fatora.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Fatora.BL.Exceptions;

namespace Fatora.BL.Services.Classes;

public class JwtTokenProviderService(IConfiguration configuration, AppDbContext dbContext) : IJwtTokenProviderService
{
    public async Task<JwtTokenResponse> GenerateToken(User user)
    {
        var jwtSettings = configuration.GetSection("JwtSettings");

        var issuer = jwtSettings["Issuer"];
        var audience = jwtSettings["Audience"];
        var key = jwtSettings["SecretKey"];
        var expiry = DateTime.UtcNow.AddMinutes(int.Parse(jwtSettings["TokenExpirationInMinutes"]!));

        var claims = new List<Claim>()
        {
           new Claim(JwtRegisteredClaimNames.Sub,user.Id.ToString()),
           new Claim(ClaimTypes.Role,user.Role.ToString()),
        };

        var descriptor = new SecurityTokenDescriptor()
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiry,
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key!)),
                SecurityAlgorithms.HmacSha256Signature
                )
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var securityToken = tokenHandler.CreateToken(descriptor);

        var rawRefreshToken = await IssueRefreshTokenAsync(user);

        return new JwtTokenResponse
        {
            AccessToken = tokenHandler.WriteToken(securityToken),
            RefreshToken = rawRefreshToken,
            Expires = expiry
        };
    }

    public async Task<JwtTokenResponse> RefreshTokenAsync(string refreshToken)
    {
        var hashedToken = HashToken(refreshToken);
        var storedToken = await dbContext.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == hashedToken);

        if (storedToken is null || storedToken.ExpiresOnUtc < DateTime.UtcNow)
        {
            throw new UnauthorizedException("Invalid or expired refresh token");
        }

        var status = UserService.ComputeAccountStatus(storedToken.User);
        if (status is "Expired" or "Suspended")
        {
            throw new UnauthorizedException($"This account is {status.ToLowerInvariant()}.", AccountStatusErrorCodes.For(status));
        }

        return await GenerateToken(storedToken.User);
    }

    public async Task LogoutAsync(Guid userId)
    {
        var refreshTokens = await dbContext.RefreshTokens.Where(r => r.UserId == userId).ToListAsync();
        dbContext.RefreshTokens.RemoveRange(refreshTokens);
        await dbContext.SaveChangesAsync();
    }

    private async Task<string> IssueRefreshTokenAsync(User user)
    {
        var existingTokens = await dbContext.RefreshTokens.Where(r => r.UserId == user.Id).ToListAsync();
        dbContext.RefreshTokens.RemoveRange(existingTokens);

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        // Only the hash is persisted - a stolen DB snapshot doesn't hand out usable refresh tokens.
        // SHA-256 (not a slow password hash) is appropriate here: this is already 256 bits of secure
        // randomness, not a guessable user-chosen secret, so it needs collision resistance, not
        // brute-force resistance.
        var refreshToken = new RefreshToken
        {
            Token = HashToken(rawToken),
            UserId = user.Id,
            ExpiresOnUtc = DateTime.UtcNow.AddDays(7)
        };

        await dbContext.RefreshTokens.AddAsync(refreshToken);
        await dbContext.SaveChangesAsync();

        return rawToken;
    }

    private static string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
