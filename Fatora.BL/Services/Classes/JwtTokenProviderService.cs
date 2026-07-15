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

        var refreshToken = await IssueRefreshTokenAsync(user);

        return new JwtTokenResponse
        {
            AccessToken = tokenHandler.WriteToken(securityToken),
            RefreshToken = refreshToken.Token,
            Expires = expiry
        };
    }

    public async Task<JwtTokenResponse?> RefreshTokenAsync(string refreshToken)
    {
        var storedToken = await dbContext.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == refreshToken);

        if (storedToken is null || storedToken.ExpiresOnUtc < DateTime.UtcNow)
        {
            return null;
        }

        return await GenerateToken(storedToken.User);
    }

    private async Task<RefreshToken> IssueRefreshTokenAsync(User user)
    {
        var existingTokens = await dbContext.RefreshTokens.Where(r => r.UserId == user.Id).ToListAsync();
        dbContext.RefreshTokens.RemoveRange(existingTokens);

        var refreshToken = new RefreshToken
        {
            Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            UserId = user.Id,
            ExpiresOnUtc = DateTime.UtcNow.AddDays(7)
        };

        await dbContext.RefreshTokens.AddAsync(refreshToken);
        await dbContext.SaveChangesAsync();

        return refreshToken;
    }
}
