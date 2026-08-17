using Fatora.API.Filters;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Classes;
using Fatora.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Xunit;

namespace Fatora.Tests;

// This is the actual enforcement point on every single authenticated
// request (not just login/refresh) - the root cause report's "device A
// must be logged out on its very next request" requirement lives here.
public class AccountStatusFilterTests
{
    private static AuthorizationFilterContext BuildContext(ClaimsPrincipal principal)
    {
        var httpContext = new DefaultHttpContext { User = principal };
        var actionContext = new Microsoft.AspNetCore.Mvc.ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
    }

    private static ClaimsPrincipal SalesRepPrincipal(Guid userId, int sessionVersion)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(ClaimTypes.Role, Role.SalesRep.ToString()),
                new Claim("sessionVersion", sessionVersion.ToString()),
            },
            authenticationType: "Test");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task OnAuthorizationAsync_TokenMatchesCurrentSessionVersion_Allows()
    {
        var db = TestSupport.NewDbContext();
        var user = TestSupport.NewUser();
        user.SessionVersion = 2;
        db.Users.Add(user);
        db.SaveChanges();

        var filter = new AccountStatusFilter(db, new FakeRepAuthService(), NullLogger<AccountStatusFilter>.Instance);
        var context = BuildContext(SalesRepPrincipal(user.Id, sessionVersion: 2));

        await filter.OnAuthorizationAsync(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public async Task OnAuthorizationAsync_StaleSessionVersion_RejectsWithSessionExpired()
    {
        var db = TestSupport.NewDbContext();
        var user = TestSupport.NewUser();
        // A newer login already bumped this to 2 - the request under test
        // carries the OLD device's token, still embedding 1 (exactly
        // "Device A" in the bug report, one request after "Device B"
        // logged in).
        user.SessionVersion = 2;
        db.Users.Add(user);
        db.SaveChanges();

        var filter = new AccountStatusFilter(db, new FakeRepAuthService(), NullLogger<AccountStatusFilter>.Instance);
        var context = BuildContext(SalesRepPrincipal(user.Id, sessionVersion: 1));

        var exception = await Assert.ThrowsAsync<ForbiddenException>(() => filter.OnAuthorizationAsync(context));

        Assert.Equal(AccountStatusErrorCodes.SessionExpired, exception.ErrorCode);
    }

    [Fact]
    public async Task OnAuthorizationAsync_TheDeviceThatJustLoggedIn_IsNeverRejectedByItsOwnRequest()
    {
        // Regression guard for the exact bug reported: after Device B logs
        // in (bumping SessionVersion to 1 and embedding 1 in its own
        // token), Device B's own very next request must be allowed, never
        // flagged as if IT were the superseded one.
        var db = TestSupport.NewDbContext();
        var user = TestSupport.NewUser();
        user.SessionVersion = 1;
        db.Users.Add(user);
        db.SaveChanges();

        var filter = new AccountStatusFilter(db, new FakeRepAuthService(), NullLogger<AccountStatusFilter>.Instance);
        var context = BuildContext(SalesRepPrincipal(user.Id, sessionVersion: 1));

        await filter.OnAuthorizationAsync(context);

        Assert.Null(context.Result);
    }
}
