using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Fatora.API.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
