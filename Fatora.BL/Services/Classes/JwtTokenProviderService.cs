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
using Microsoft.Extensions.Logging;

namespace Fatora.BL.Services.Classes;

public class JwtTokenProviderService(
    IConfiguration configuration,
    AppDbContext dbContext,
    IRepAuthService repAuthService,
    ISubAdminAuthService subAdminAuthService,
    ILogger<JwtTokenProviderService> logger) : IJwtTokenProviderService
{
    // How long a just-rotated refresh token still gets a fresh pair instead
    // of a rejection. Exists for one specific race: IssueRefreshTokenAsync
    // commits the rotation before the HTTP response carrying the new token
    // is even built, so if that response never reaches the client (a
    // timeout - ApiClient's 30s budget sits close to Render's own
    // documented ~24-50s cold-start wake), the client's only move is to
    // retry with the token it still has on disk, which the server has
    // already invalidated a moment earlier. The client cannot tell "my
    // first attempt is still in flight" apart from "it already succeeded
    // and I never heard back" - from its side those look identical. Without
    // this window, that legitimate retry was rejected exactly like a truly
    // dead token, and the frontend's own bug (fixed alongside this) turned
    // that into a silent, unexplained logout instead of a visible one.
    // Shared by RepAuthService and SubAdminAuthService, which mirror this
    // exact rotation shape for their own sessions.
    //
    // Deliberately short: it only ever revives a token that's already been
    // superseded, never the one currently in active use, so it adds no
    // meaningful window for a genuinely stolen token - an attacker would
    // need to have captured it in the same narrow moment the legitimate
    // device was naturally rotating it anyway.
    public static readonly TimeSpan RefreshRotationGrace = TimeSpan.FromSeconds(90);

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
           // Same "sessionVersion" claim shape Rep/SubAdmin tokens already
           // carry - see User.SessionVersion and AccountStatusFilter's
           // User branch, which rejects a token whose embedded value no
           // longer matches the account's current one.
           new Claim("sessionVersion", user.SessionVersion.ToString()),
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

        // A Rep or SubAdmin session's raw refresh token never matches a
        // RefreshTokens row (separate tables - see RepRefreshToken.cs /
        // SubAdminRefreshToken.cs), so this is how all three session kinds
        // refresh through the same POST /account/refresh-token endpoint
        // without the frontend having to know or care which kind it's
        // holding.
        if (storedToken is null)
        {
            var repResult = await repAuthService.TryRefreshAsync(refreshToken);
            if (repResult is not null)
            {
                return repResult;
            }

            var subAdminResult = await subAdminAuthService.TryRefreshAsync(refreshToken);
            if (subAdminResult is not null)
            {
                return subAdminResult;
            }
        }

        if (storedToken is null || storedToken.ExpiresOnUtc < DateTime.UtcNow)
        {
            throw new UnauthorizedException("Invalid or expired refresh token", AccountStatusErrorCodes.SessionExpired);
        }

        var status = UserService.ComputeAccountStatus(storedToken.User);
        if (status is "Expired" or "Suspended")
        {
            throw new UnauthorizedException($"This account is {status.ToLowerInvariant()}.", AccountStatusErrorCodes.For(status));
        }

        // Rejects a device whose session has been superseded by a newer
        // login on this same account (see LoginService.Login bumping
        // User.SessionVersion). Unlike the ReplacedAtUtc grace window
        // below - which only ever protects a legitimate retry of this SAME
        // session's own rotation - there is no grace period here: a
        // different, newer session is now the account's only valid one.
        if (storedToken.IssuedAtSessionVersion != storedToken.User.SessionVersion)
        {
            logger.LogInformation(
                "Refresh rejected - session superseded. AccountId: {AccountId}, TokenSessionVersion: {TokenSessionVersion}, CurrentSessionVersion: {CurrentSessionVersion}",
                storedToken.UserId,
                storedToken.IssuedAtSessionVersion,
                storedToken.User.SessionVersion);
            throw new UnauthorizedException("Invalid or expired refresh token", AccountStatusErrorCodes.SessionExpired);
        }

        // A superseded token gets exactly one more chance to reissue, within
        // RefreshRotationGrace - past it, it's exactly as dead as any other
        // invalid token. See the field comment for why this exists.
        if (storedToken.ReplacedAtUtc is { } replacedAt && replacedAt < DateTime.UtcNow - RefreshRotationGrace)
        {
            throw new UnauthorizedException("Invalid or expired refresh token", AccountStatusErrorCodes.SessionExpired);
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
        var now = DateTime.UtcNow;

        foreach (var existing in existingTokens)
        {
            // Already past its own grace window - exactly as dead as one
            // that was never superseded, so delete it here instead of
            // leaving it to accumulate forever. Piggybacks on the
            // per-user-scoped query above, which already runs on every
            // rotation.
            if (existing.ReplacedAtUtc is { } replacedAt && replacedAt < now - RefreshRotationGrace)
            {
                dbContext.RefreshTokens.Remove(existing);
            }
            else
            {
                // ??=, not =: this loop runs on EVERY rotation for this user,
                // touching every one of their outstanding rows, not just the
                // one presented in this particular request. A plain
                // assignment would push the timestamp forward on unrelated
                // activity (a second device, a background refresh) every
                // time it ran - stamping "now" on a row already stamped 85
                // seconds ago restarts its 90-second clock, so continuous
                // account activity could keep an old row perpetually inside
                // its grace window instead of the bounded 90s the field
                // comment promises. Only the first supersession should ever
                // set this.
                existing.ReplacedAtUtc ??= now;
            }
        }

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        // Only the hash is persisted - a stolen DB snapshot doesn't hand out usable refresh tokens.
        // SHA-256 (not a slow password hash) is appropriate here: this is already 256 bits of secure
        // randomness, not a guessable user-chosen secret, so it needs collision resistance, not
        // brute-force resistance.
        var refreshToken = new RefreshToken
        {
            Token = HashToken(rawToken),
            UserId = user.Id,
            ExpiresOnUtc = now.AddDays(7),
            IssuedAtSessionVersion = user.SessionVersion
        };

        await dbContext.RefreshTokens.AddAsync(refreshToken);
        await dbContext.SaveChangesAsync();

        return rawToken;
    }

    private static string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
