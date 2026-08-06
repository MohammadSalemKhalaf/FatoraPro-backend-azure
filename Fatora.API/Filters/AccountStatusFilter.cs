using Fatora.API.Extensions;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Abstractions;
using Fatora.BL.Services.Classes;
using Fatora.DAL.Data;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Fatora.API.Filters;

// Runs on every authenticated request, not just login/refresh - an expired or suspended
// SalesRep is locked out immediately, on whatever call they make next, not only at their
// next sign-in. Admins are exempt: they don't have a subscription concept.
public sealed class AccountStatusFilter(AppDbContext dbContext, IRepAuthService repAuthService) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var httpContext = context.HttpContext;

        if (httpContext.User.Identity?.IsAuthenticated != true || httpContext.User.IsInRole("Admin"))
        {
            return;
        }

        // A RepPendingSync token (see RepAuthService.GeneratePendingSyncToken)
        // is deliberately exempt from every check below - it exists
        // specifically so an already-blocked rep still has one narrow
        // channel to hand over unsynced offline work. It can't reach
        // anything else anyway: every other Rep/SalesRep-gated endpoint's
        // own [Authorize(Roles = ...)] already rejects this role outright,
        // since "RepPendingSync" satisfies neither.
        if (httpContext.User.IsInRole("RepPendingSync"))
        {
            return;
        }

        // GetUserId() resolves to the Rep's own id for a Rep session (its
        // "sub" claim), not a Users.Id - looking that up against Users
        // below would just silently no-op, which is why a Rep session used
        // to never get caught by this filter for anything at all, not even
        // its owner's own Expired/Suspended state. Handled as its own branch
        // instead, with its own two checks: is this specific sub-account
        // still logged in at all (IsActive/SessionVersion - see
        // RepService.LogoutAsync/DeactivateAsync), and separately, is the
        // owning business itself still in good standing.
        if (httpContext.User.IsInRole("Rep"))
        {
            var repId = httpContext.User.GetRepIdOrNull();
            var rep = repId is null
                ? null
                : await dbContext.Reps.AsNoTracking()
                    .Include(r => r.OwnerUser)
                    .FirstOrDefaultAsync(r => r.Id == repId);

            if (rep is null)
            {
                return;
            }

            var tokenSessionVersion = int.Parse(httpContext.User.FindFirstValue("sessionVersion") ?? "0");
            if (!rep.IsActive || tokenSessionVersion != rep.SessionVersion)
            {
                // Same reasoning as TryRefreshAsync's equivalent rejection:
                // the block itself is unconditional, but if this device
                // still has unsynced offline work, this narrow, short-lived
                // token is its one remaining chance to hand it over.
                throw new ForbiddenException(
                    "This rep session has ended.",
                    AccountStatusErrorCodes.RepSessionEnded,
                    new Dictionary<string, object> { ["pendingSyncToken"] = repAuthService.GeneratePendingSyncToken(rep) });
            }

            var ownerStatus = UserService.ComputeAccountStatus(rep.OwnerUser);
            if (ownerStatus is "Expired" or "Suspended")
            {
                throw new ForbiddenException($"This account is {ownerStatus.ToLowerInvariant()} and can no longer access the system.", AccountStatusErrorCodes.For(ownerStatus));
            }

            return;
        }

        var userId = httpContext.User.GetUserId();
        var user = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null)
        {
            return;
        }

        var status = UserService.ComputeAccountStatus(user);
        if (status is "Expired" or "Suspended")
        {
            throw new ForbiddenException($"This account is {status.ToLowerInvariant()} and can no longer access the system.", AccountStatusErrorCodes.For(status));
        }
    }
}
