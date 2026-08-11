using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Fatora.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fatora.BL.Services.Classes;

public class AdminReportService(AppDbContext dbContext) : IAdminReportService
{
    // How many of a SubAdmin's own activations to surface - a fixed count,
    // deliberately not affected by the period filter (see
    // SubAdminBreakdownResponse.RecentActivations).
    private const int RecentActivationsCount = 10;

    // ExpiringSoon must mean genuinely "about to expire," not just "the
    // soonest of however many happen to exist" - a subscriber whose
    // subscription still has months left must never appear here just
    // because everyone else's ends even further out.
    private const int ExpiringSoonWindowDays = 3;

    public async Task<AdminDashboardResponse> GetDashboardAsync(AdminReportPeriod period, Guid? actingSubAdminId)
    {
        var cutoff = GetCutoff(period);

        if (actingSubAdminId is not null)
        {
            var ownBreakdown = await BuildSubAdminBreakdownAsync(actingSubAdminId.Value);
            return new AdminDashboardResponse
            {
                Platform = null,
                SubAdminBreakdown = ownBreakdown is null ? [] : [ownBreakdown]
            };
        }

        // Only SalesRep-role rows are actual subscribers - the Admin's own
        // account(s) never count toward the platform totals below.
        var subscribers = await dbContext.Users.Where(u => u.Role == Role.SalesRep).ToListAsync();

        var platform = new PlatformStatsResponse
        {
            TotalSubscribers = subscribers.Count,
            AccountStatusBreakdown = subscribers
                .GroupBy(UserService.ComputeAccountStatus)
                .ToDictionary(g => g.Key, g => g.Count()),
            SubscriptionTypeBreakdown = subscribers
                .GroupBy(u => u.SubscriptionType.ToString())
                .ToDictionary(g => g.Key, g => g.Count()),
            NewSignupsInPeriod = cutoff is null
                ? subscribers.Count
                : subscribers.Count(u => u.CreatedAt >= cutoff),
            ActivationsInPeriod = cutoff is null
                ? await dbContext.SubscriptionActivations.CountAsync()
                : await dbContext.SubscriptionActivations.CountAsync(a => a.ActivatedAt >= cutoff)
        };

        var activeSubAdminIds = await dbContext.SubAdmins
            .Where(s => !s.IsHidden)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.Id)
            .ToListAsync();

        var subAdminBreakdown = new List<SubAdminBreakdownResponse>();
        foreach (var subAdminId in activeSubAdminIds)
        {
            var breakdown = await BuildSubAdminBreakdownAsync(subAdminId);
            if (breakdown is not null)
            {
                subAdminBreakdown.Add(breakdown);
            }
        }

        return new AdminDashboardResponse { Platform = platform, SubAdminBreakdown = subAdminBreakdown };
    }

    private async Task<SubAdminBreakdownResponse?> BuildSubAdminBreakdownAsync(Guid subAdminId)
    {
        var subAdmin = await dbContext.SubAdmins.FirstOrDefaultAsync(s => s.Id == subAdminId);
        if (subAdmin is null)
        {
            return null;
        }

        var managedSubscribers = await dbContext.Users
            .Where(u => u.ManagedBySubAdminId == subAdminId)
            .ToListAsync();

        var recentActivations = await dbContext.SubscriptionActivations
            .Where(a => a.PerformedBySubAdminId == subAdminId)
            .Include(a => a.User)
            .OrderByDescending(a => a.ActivatedAt)
            .Take(RecentActivationsCount)
            .Select(a => new RecentActivationResponse
            {
                Id = a.Id,
                SubscriberId = a.UserId,
                SubscriberUserName = a.User.UserName,
                SubscriptionType = a.SubscriptionType.ToString(),
                CustomMonths = a.CustomMonths,
                ActivatedAt = a.ActivatedAt
            })
            .ToListAsync();

        return new SubAdminBreakdownResponse
        {
            SubAdminId = subAdmin.Id,
            SubAdminName = subAdmin.Name,
            ManagedSubscriberCount = managedSubscribers.Count,
            SubscriptionTypeBreakdown = managedSubscribers
                .GroupBy(u => u.SubscriptionType.ToString())
                .ToDictionary(g => g.Key, g => g.Count()),
            RecentActivations = recentActivations,
            RecentlyJoined = managedSubscribers
                .OrderByDescending(u => u.CreatedAt)
                .Take(RecentActivationsCount)
                .Select(u => new RecentSubscriberResponse
                {
                    Id = u.Id,
                    UserName = u.UserName,
                    Name = u.Name,
                    CreatedAt = u.CreatedAt
                })
                .ToList(),
            ExpiringSoon = managedSubscribers
                .Where(u => u.SubscriptionEnd != null
                    && u.SubscriptionEnd > DateTime.UtcNow
                    && u.SubscriptionEnd <= DateTime.UtcNow.AddDays(ExpiringSoonWindowDays))
                .OrderBy(u => u.SubscriptionEnd)
                .Take(RecentActivationsCount)
                .Select(u => new ExpiringSubscriberResponse
                {
                    Id = u.Id,
                    UserName = u.UserName,
                    Name = u.Name,
                    SubscriptionEnd = u.SubscriptionEnd!.Value
                })
                .ToList()
        };
    }

    private static DateTime? GetCutoff(AdminReportPeriod period)
    {
        var now = DateTime.UtcNow;
        return period switch
        {
            // A calendar-day boundary (UTC midnight), mirroring the admin
            // frontend's own ReportPeriod.today cutoff logic.
            AdminReportPeriod.Today => new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc),
            AdminReportPeriod.Week => now.AddDays(-7),
            AdminReportPeriod.Month => now.AddDays(-30),
            AdminReportPeriod.Year => now.AddDays(-365),
            AdminReportPeriod.All => null,
            _ => throw new ArgumentOutOfRangeException(nameof(period))
        };
    }
}
