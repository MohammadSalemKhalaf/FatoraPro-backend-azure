using Fatora.BL.DTOs.Responses;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Fatora.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.IdentityModel.Tokens.Jwt;

namespace Fatora.Tests;

// Decodes an access token's own claims so a test can assert on exactly
// what AccountStatusFilter/RefreshTokenAsync would see and compare
// against the DB - no signature verification needed here, this is only
// ever reading back a token this same test process just issued.
public static class TestJwt
{
    public static string? ReadClaim(string accessToken, string claimType) =>
        new JwtSecurityTokenHandler()
            .ReadJwtToken(accessToken)
            .Claims.FirstOrDefault(c => c.Type == claimType)?.Value;
}

// Plain string-equality "hasher" - deterministic and fast for tests, never
// used outside this project. Matches IPasswordHasherService's real
// signature exactly so LoginService needs no special-casing to be tested.
public sealed class FakePasswordHasherService : IPasswordHasherService
{
    public string Hash(User user, string password) => password;

    public bool Verify(User user, string password, string hashedPassword) => password == hashedPassword;
}

// Neither Rep nor SubAdmin refresh tokens are relevant to the User-session
// tests in this project - both TryRefreshAsync overrides return null
// (their real, documented "not mine" signal), and the login/pending-sync
// methods are never called from the paths under test here.
public sealed class FakeRepAuthService : IRepAuthService
{
    public Task<JwtTokenResponse> LoginByQrAsync(string qrToken) => throw new NotImplementedException();

    public Task<JwtTokenResponse?> TryRefreshAsync(string refreshToken) => Task.FromResult<JwtTokenResponse?>(null);

    public string GeneratePendingSyncToken(Rep rep) => throw new NotImplementedException();
}

public sealed class FakeSubAdminAuthService : ISubAdminAuthService
{
    public Task<JwtTokenResponse> LoginByQrAsync(string qrToken) => throw new NotImplementedException();

    public Task<JwtTokenResponse?> TryRefreshAsync(string refreshToken) => Task.FromResult<JwtTokenResponse?>(null);
}

public static class TestSupport
{
    // A fresh, isolated in-memory database per call - each test gets its
    // own store (unique Guid name), so tests never see each other's rows
    // even when they run in parallel.
    public static AppDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    // Mirrors appsettings.json's JwtSettings shape exactly - a fixed test
    // secret key long enough for HMAC-SHA256, never used outside this
    // project.
    public static IConfiguration JwtConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Issuer"] = "fatora-tests",
                ["JwtSettings:Audience"] = "fatora-tests",
                ["JwtSettings:SecretKey"] = "this-is-a-test-only-secret-key-not-used-anywhere-real",
                ["JwtSettings:TokenExpirationInMinutes"] = "10",
            })
            .Build();

    public static User NewUser(string password = "password123") => new()
    {
        UserName = $"user-{Guid.NewGuid():N}",
        Password = password,
        Name = "Test Owner",
        PhoneNumber = "0599000000",
        Role = Role.SalesRep,
        SubscriptionType = SubscriptionType.Trial,
        SubscriptionStart = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
    };
}
