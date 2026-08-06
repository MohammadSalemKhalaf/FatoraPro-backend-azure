using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Fatora.API.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    // The one thing every Orders/Customers/Products/Sync/Reports controller
    // action should call instead of GetUserId() directly: for a normal
    // User session this is just their own id (unchanged), but for a Rep
    // session it resolves to the owning business's id (the "ownerId" claim -
    // see RepAuthService), so every existing query/write that filters or
    // sets UserId keeps landing in the right place without knowing Reps
    // exist at all.
    public static Guid GetEffectiveOwnerId(this ClaimsPrincipal user) =>
        user.IsInRole("Rep") ? Guid.Parse(user.FindFirstValue("ownerId")!) : user.GetUserId();

    // Null for a normal User session. For a Rep session, the "sub" claim IS
    // the rep's own id (see RepAuthService.GenerateTokenAsync) - this is
    // what CreatedByRepId gets set to on write, and what a Rep's own reads
    // are scoped down to.
    public static Guid? GetRepIdOrNull(this ClaimsPrincipal user) =>
        user.IsInRole("Rep") ? user.GetUserId() : null;
}
