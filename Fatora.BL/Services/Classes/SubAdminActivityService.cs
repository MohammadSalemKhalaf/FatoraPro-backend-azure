using Fatora.BL.DTOs.Responses;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fatora.BL.Services.Classes;

public class SubAdminActivityService(AppDbContext dbContext) : ISubAdminActivityService
{
    public async Task<SubAdminRouteResponse> GetRouteAsync(Guid subAdminId, DateTime startUtc, DateTime endUtc)
    {
        // CreatedAt/ActivatedAt are `timestamp with time zone` in Postgres -
        // same reasoning as RepActivityService.GetRouteAsync.
        var startOfDay = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        var endOfDay = DateTime.SpecifyKind(endUtc, DateTimeKind.Utc);

        var activations = await dbContext.SubscriptionActivations
            .AsNoTracking()
            .Where(a => a.PerformedBySubAdminId == subAdminId && a.ActivatedAt >= startOfDay && a.ActivatedAt < endOfDay)
            .Select(a => new
            {
                a.Id,
                a.Latitude,
                a.Longitude,
                CreatedAt = a.ActivatedAt,
                Label = a.SubscriptionType.ToString(),
                SubscriberName = a.User.Name
            })
            .ToListAsync();

        var accountActions = await dbContext.SubAdminAccountActions
            .AsNoTracking()
            .Where(a => a.PerformedBySubAdminId == subAdminId && a.CreatedAt >= startOfDay && a.CreatedAt < endOfDay)
            .Select(a => new
            {
                a.Id,
                a.Latitude,
                a.Longitude,
                a.CreatedAt,
                Type = a.ActionType.ToString(),
                SubscriberName = a.User.Name
            })
            .ToListAsync();

        var unlocatedCount = activations.Count(a => a.Latitude is null) + accountActions.Count(a => a.Latitude is null);

        var points = activations
            .Where(a => a.Latitude is not null && a.Longitude is not null)
            .Select(a => new SubAdminRoutePointResponse
            {
                Id = a.Id,
                Type = "Activated",
                Latitude = a.Latitude!.Value,
                Longitude = a.Longitude!.Value,
                SubscriberName = a.SubscriberName,
                Label = a.Label,
                CreatedAt = a.CreatedAt
            })
            .Concat(accountActions
                .Where(a => a.Latitude is not null && a.Longitude is not null)
                .Select(a => new SubAdminRoutePointResponse
                {
                    Id = a.Id,
                    Type = a.Type,
                    Latitude = a.Latitude!.Value,
                    Longitude = a.Longitude!.Value,
                    SubscriberName = a.SubscriberName,
                    Label = string.Empty,
                    CreatedAt = a.CreatedAt
                }))
            .OrderBy(p => p.CreatedAt)
            .ToList();

        for (var i = 0; i < points.Count; i++)
        {
            points[i].Sequence = i + 1;
        }

        return new SubAdminRouteResponse { Points = points, UnlocatedCount = unlocatedCount };
    }

    public async Task<List<SubAdminActivityItemResponse>> GetActivityFeedAsync()
    {
        var since = DateTime.UtcNow.AddHours(-24);

        // PerformedBySubAdminId != null excludes the top Admin's own direct
        // actions - this feed is specifically "what have my SubAdmins been
        // doing", same reasoning as RepActivityService.GetActivityFeedAsync
        // excluding the owner's own direct Orders/Receipts.
        var activations = await dbContext.SubscriptionActivations
            .AsNoTracking()
            .Include(a => a.User)
            .Include(a => a.PerformedBySubAdmin)
            .Where(a => a.PerformedBySubAdminId != null && a.ActivatedAt >= since)
            .ToListAsync();

        var accountActions = await dbContext.SubAdminAccountActions
            .AsNoTracking()
            .Include(a => a.User)
            .Include(a => a.PerformedBySubAdmin)
            .Where(a => a.PerformedBySubAdminId != null && a.CreatedAt >= since)
            .ToListAsync();

        return activations
            .Select(a => new SubAdminActivityItemResponse
            {
                Id = a.Id,
                SubAdminId = a.PerformedBySubAdminId!.Value,
                SubAdminName = a.PerformedBySubAdmin!.Name,
                Type = "Activated",
                SubscriberName = a.User.Name,
                Detail = a.SubscriptionType.ToString(),
                CreatedAt = a.ActivatedAt,
                Latitude = a.Latitude,
                Longitude = a.Longitude
            })
            .Concat(accountActions.Select(a => new SubAdminActivityItemResponse
            {
                Id = a.Id,
                SubAdminId = a.PerformedBySubAdminId!.Value,
                SubAdminName = a.PerformedBySubAdmin!.Name,
                Type = a.ActionType.ToString(),
                SubscriberName = a.User.Name,
                Detail = string.Empty,
                CreatedAt = a.CreatedAt,
                Latitude = a.Latitude,
                Longitude = a.Longitude
            }))
            .OrderByDescending(item => item.CreatedAt)
            .ToList();
    }
}
