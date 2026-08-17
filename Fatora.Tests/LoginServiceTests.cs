using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Classes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fatora.Tests;

// Covers the actual bug report this session: a new login on the owner/
// SalesRep (User) account must supersede whatever session was active
// before it, and that supersession must be visible both in the stored
// User row (SessionVersion) and in the freshly-issued token's own claim -
// AccountStatusFilter and JwtTokenProviderService.RefreshTokenAsync both
// rely on comparing those two values, so this is the root of the whole
// fix.
public class LoginServiceTests
{
    private static (LoginService service, Fatora.DAL.Data.AppDbContext db, Fatora.DAL.Entities.User user) Build(string password = "password123")
    {
        var db = TestSupport.NewDbContext();
        var user = TestSupport.NewUser(password);
        db.Users.Add(user);
        db.SaveChanges();

        var jwtProvider = new JwtTokenProviderService(
            TestSupport.JwtConfiguration(),
            db,
            new FakeRepAuthService(),
            new FakeSubAdminAuthService(),
            NullLogger<JwtTokenProviderService>.Instance);

        var service = new LoginService(
            db,
            new FakePasswordHasherService(),
            jwtProvider,
            NullLogger<LoginService>.Instance);

        return (service, db, user);
    }

    [Fact]
    public async Task Login_OnFirstEverLogin_BumpsSessionVersionFromZeroToOne()
    {
        var (service, db, user) = Build();

        await service.Login(new LoginRequest(user.UserName, "password123"));

        var stored = db.Users.Single(u => u.Id == user.Id);
        Assert.Equal(1, stored.SessionVersion);
    }

    [Fact]
    public async Task Login_ASecondTime_BumpsSessionVersionAgain_SupersedingTheFirstDevice()
    {
        var (service, db, user) = Build();

        // Device A.
        var deviceAToken = await service.Login(new LoginRequest(user.UserName, "password123"));
        // Device B - the second login the whole bug report is about.
        var deviceBToken = await service.Login(new LoginRequest(user.UserName, "password123"));

        var stored = db.Users.Single(u => u.Id == user.Id);
        Assert.Equal(2, stored.SessionVersion);

        var deviceASessionVersion = TestJwt.ReadClaim(deviceAToken.AccessToken, "sessionVersion");
        var deviceBSessionVersion = TestJwt.ReadClaim(deviceBToken.AccessToken, "sessionVersion");

        // The two tokens must never embed the same session version -
        // that's the exact claim AccountStatusFilter compares against the
        // live DB value to decide whether a device's token is still
        // current.
        Assert.Equal("1", deviceASessionVersion);
        Assert.Equal("2", deviceBSessionVersion);
        Assert.NotEqual(deviceASessionVersion, deviceBSessionVersion);

        // Device B's own token must always match the CURRENT stored
        // version - a fresh login is never itself superseded by its own
        // sign-in.
        Assert.Equal(stored.SessionVersion.ToString(), deviceBSessionVersion);
    }

    [Fact]
    public async Task Login_WithWrongPassword_DoesNotBumpSessionVersion()
    {
        var (service, db, user) = Build();

        await Assert.ThrowsAsync<Fatora.BL.Exceptions.UnauthorizedException>(() =>
            service.Login(new LoginRequest(user.UserName, "wrong-password")));

        var stored = db.Users.Single(u => u.Id == user.Id);
        Assert.Equal(0, stored.SessionVersion);
    }
}
