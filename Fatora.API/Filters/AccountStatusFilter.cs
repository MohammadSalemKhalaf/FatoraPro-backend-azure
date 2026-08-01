using Fatora.API.Extensions;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Classes;
using Fatora.DAL.Data;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Fatora.API.Filters;

// Runs on every authenticated request, not just login/refresh - an expired or suspended
// SalesRep is locked out immediately, on whatever call they make next, not only at their
// next sign-in. Admins are exempt: they don't have a subscription concept.
public sealed class AccountStatusFilter(AppDbContext dbContext) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var httpContext = context.HttpContext;

        if (httpContext.User.Identity?.IsAuthenticated != true || httpContext.User.IsInRole("Admin"))
        {
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
