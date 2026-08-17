using Fatora.BL.Exceptions;
using Fatora.BL.Services.Classes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fatora.Tests;

// RefreshTokenAsync is the second enforcement point (besides
// AccountStatusFilter) that has to reject a superseded device - a device
// whose access token already expired has no other way to notice its
// session ended except by trying to refresh.
public class JwtTokenProviderServiceTests
{
    private static (JwtTokenProviderService provider, Fatora.DAL.Data.AppDbContext db, Fatora.DAL.Entities.User user) Build()
    {
        var db = TestSupport.NewDbContext();
        var user = TestSupport.NewUser();
        db.Users.Add(user);
        db.SaveChanges();

        var provider = new JwtTokenProviderService(
            TestSupport.JwtConfiguration(),
            db,
            new FakeRepAuthService(),
            new FakeSubAdminAuthService(),
            NullLogger<JwtTokenProviderService>.Instance);

        return (provider, db, user);
    }

    [Fact]
    public async Task RefreshTokenAsync_WithTheCurrentSessionsToken_Succeeds()
    {
        var (provider, db, user) = Build();
        var issued = await provider.GenerateToken(user);

        var refreshed = await provider.RefreshTokenAsync(issued.RefreshToken);

        Assert.NotNull(refreshed.AccessToken);
    }

    [Fact]
    public async Task RefreshTokenAsync_WithASupersededDevicesToken_ThrowsSessionExpired()
    {
        var (provider, db, user) = Build();

        // Device A gets a refresh token bound to session version 0 (the
        // fresh user's starting value).
        var deviceAIssued = await provider.GenerateToken(user);

        // Device B logs in - the exact effect LoginService.Login has, done
        // directly here to isolate this test to JwtTokenProviderService's
        // own behavior.
        user.SessionVersion++;
        db.SaveChanges();
        await provider.GenerateToken(user);

        // Device A, unaware it's been superseded, tries to refresh with
        // its now-stale token.
        var exception = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            provider.RefreshTokenAsync(deviceAIssued.RefreshToken));

        Assert.Equal(AccountStatusErrorCodes.SessionExpired, exception.ErrorCode);
    }

    [Fact]
    public async Task RefreshTokenAsync_AfterSupersession_TheNewDevicesOwnTokenStillWorks()
    {
        var (provider, db, user) = Build();

        await provider.GenerateToken(user); // Device A.
        user.SessionVersion++;
        db.SaveChanges();
        var deviceBIssued = await provider.GenerateToken(user); // Device B.

        // Device B refreshing its own, current token must never be
        // affected by device A's supersession.
        var refreshed = await provider.RefreshTokenAsync(deviceBIssued.RefreshToken);

        Assert.NotNull(refreshed.AccessToken);
    }
}
