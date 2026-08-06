using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;

namespace Fatora.BL.Services.Classes;

public class ReportService(AppDbContext dbContext) : IReportService
{
    public async Task<List<SalesBucketResponse>> GetSalesAsync(Guid userId, SummaryPeriod period, Guid? scopeToRepId = null)
    {
        var orders = await LoadOrdersForPeriod(userId, period, scopeToRepId);

        return period switch
        {
            SummaryPeriod.Week => BuildDailyBuckets(orders, days: 7),
            SummaryPeriod.Month => BuildDailyBuckets(orders, days: 30),
            SummaryPeriod.Year => BuildMonthlyBuckets(orders, months: 12),
            _ => BuildSparseMonthlyBuckets(orders)
        };
    }

    public async Task<List<ItemReportResponse>> GetItemsAsync(Guid userId, SummaryPeriod period, ItemsSortBy sortBy, Guid? scopeToRepId = null)
    {
        var cutoff = GetCutoff(period);

        // A returned invoice's items weren't ultimately sold - excluded here
        // the same way LoadOrdersForPeriod excludes the invoice itself from
        // sales/top-clients.
        var query = dbContext.OrderItems
            .Include(oi => oi.Order)
            .Include(oi => oi.Product)
            .Where(oi => oi.Order.UserId == userId && !oi.Order.IsReturned);

        if (cutoff is not null)
        {
            query = query.Where(oi => oi.Order.CreatedAt >= cutoff);
        }

        // Items are only ever reported through the orders that sold them,
        // so this follows the same rule as every other rep-scoped list: an
        // order (and therefore its line items) belongs exclusively to
        // whichever Rep created it - see LoadOrdersForPeriod.
        if (scopeToRepId is not null)
        {
            query = query.Where(oi => oi.Order.CreatedByRepId == scopeToRepId);
        }

        var orderItems = await query.ToListAsync();

        var grouped = orderItems
            .GroupBy(oi => oi.ProductId)
            .Select(g => new ItemReportResponse
            {
                ProductId = g.Key,
                ProductName = g.First().Product.Name,
                ProductImageUrl = g.First().Product.ImageUrl,
                QuantitySold = g.Sum(oi => oi.Quantity),
                TotalValue = g.Sum(oi => oi.UnitPrice * oi.Quantity)
            });

        grouped = sortBy == ItemsSortBy.MostSold
            ? grouped.OrderByDescending(i => i.QuantitySold)
            : grouped.OrderByDescending(i => i.TotalValue);

        return grouped.Take(10).ToList();
    }

    public async Task<List<ClientReportResponse>> GetClientsAsync(Guid userId, SummaryPeriod period, ClientsSortBy sortBy, Guid? scopeToRepId = null)
    {
        var orders = await LoadOrdersForPeriod(userId, period, scopeToRepId);

        var grouped = orders
            .GroupBy(o => o.CustomerId)
            .Select(g => new ClientReportResponse
            {
                CustomerId = g.Key,
                CustomerName = g.First().Customer.Name,
                InvoiceCount = g.Count(),
                TotalValue = g.Sum(o => o.Total)
            });

        grouped = sortBy == ClientsSortBy.MostInvoices
            ? grouped.OrderByDescending(c => c.InvoiceCount)
            : grouped.OrderByDescending(c => c.TotalValue);

        return grouped.Take(10).ToList();
    }

    // scopeToRepId null means "the owner, every order" - non-null means
    // "only this Rep's own orders," same exclusive-ownership rule
    // OrdersController.GetAll already applies (an order belongs to whoever
    // raised it, independent of that Rep's customer/product access mode).
    // A returned invoice is excluded entirely - its sale was reversed, so
    // it contributes nothing to sales totals or "top clients by value"
    // (the already-restored stock and cleared debt are handled elsewhere,
    // see Order.RemainingBalance/OrderService.ReturnAsync).
    private async Task<List<Order>> LoadOrdersForPeriod(Guid userId, SummaryPeriod period, Guid? scopeToRepId = null)
    {
        var cutoff = GetCutoff(period);

        var query = dbContext.Orders.Include(o => o.Customer)
            .Where(o => o.UserId == userId && !o.IsReturned
                && (scopeToRepId == null || o.CreatedByRepId == scopeToRepId));

        if (cutoff is not null)
        {
            query = query.Where(o => o.CreatedAt >= cutoff);
        }

        return await query.ToListAsync();
    }

    private static DateTime? GetCutoff(SummaryPeriod period) => period switch
    {
        SummaryPeriod.Week => DateTime.UtcNow.AddDays(-7),
        SummaryPeriod.Month => DateTime.UtcNow.AddDays(-30),
        SummaryPeriod.Year => DateTime.UtcNow.AddDays(-365),
        _ => null
    };

    private static List<SalesBucketResponse> BuildDailyBuckets(List<Order> orders, int days)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var totalsByDay = orders
            .GroupBy(o => DateOnly.FromDateTime(o.CreatedAt))
            .ToDictionary(g => g.Key, g => g.Sum(o => o.Total));

        var buckets = new List<SalesBucketResponse>();

        for (var i = days - 1; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            buckets.Add(new SalesBucketResponse
            {
                Label = day.ToString("yyyy-MM-dd"),
                Amount = totalsByDay.GetValueOrDefault(day, 0m)
            });
        }

        return buckets;
    }

    private static List<SalesBucketResponse> BuildMonthlyBuckets(List<Order> orders, int months)
    {
        var currentMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        var totalsByMonth = orders
            .GroupBy(o => new DateOnly(o.CreatedAt.Year, o.CreatedAt.Month, 1))
            .ToDictionary(g => g.Key, g => g.Sum(o => o.Total));

        var buckets = new List<SalesBucketResponse>();

        for (var i = months - 1; i >= 0; i--)
        {
            var month = currentMonth.AddMonths(-i);
            buckets.Add(new SalesBucketResponse
            {
                Label = month.ToString("yyyy-MM"),
                Amount = totalsByMonth.GetValueOrDefault(month, 0m)
            });
        }

        return buckets;
    }

    private static List<SalesBucketResponse> BuildSparseMonthlyBuckets(List<Order> orders)
    {
        return orders
            .GroupBy(o => new DateOnly(o.CreatedAt.Year, o.CreatedAt.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g => new SalesBucketResponse
            {
                Label = g.Key.ToString("yyyy-MM"),
                Amount = g.Sum(o => o.Total)
            })
            .ToList();
    }
}
