using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;

namespace Fatora.BL.Services.Classes;

public class OrderService(AppDbContext dbContext) : IOrderService
{
    // The Flutter client stores money as `double`, so a "pay the exact
    // remaining balance" round-trip can come back a fraction of a cent off
    // from the `decimal` value that was quoted. Tolerating anything under a
    // cent keeps that drift from blocking a legitimate full payment or
    // leaving an order stuck as "PartiallyPaid" after one.
    private const decimal AmountTolerance = 0.01m;


    public async Task<OrderResponse> CreateAsync(Guid userId, CreateOrderRequest request)
    {
        var customer = await FindOwnedActiveCustomer(userId, request.CustomerId);

        if (customer is null)
        {
            throw new NotFoundException(nameof(Customer), request.CustomerId);
        }

        var productsById = await LoadOwnedActiveProducts(userId, request.Items);

        if (productsById is null)
        {
            throw new NotFoundException("One or more products", string.Join(",", request.Items.Select(i => i.ProductId)));
        }

        var order = new Order
        {
            InvoiceNumber = await GenerateInvoiceNumberAsync(userId),
            CustomerId = customer.Id,
            UserId = userId,
            DueDate = request.DueDate,
            Discount = request.Discount,
            Notes = request.Notes,
            OrderItems = request.Items.Select(i => new OrderItem
            {
                ProductId = i.ProductId,
                Product = productsById[i.ProductId],
                Quantity = i.Quantity,
                UnitPrice = productsById[i.ProductId].SellPrice
            }).ToList()
        };

        ValidateCashDiscount(order.OrderItems, request.Discount, request.CashDiscount);
        order.CashDiscount = request.CashDiscount;

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        order.Customer = customer;
        return ToResponse(order);
    }

    public async Task<List<OrderResponse>> GetAllAsync(Guid userId)
    {
        var orders = await dbContext.Orders
            .Include(o => o.Customer)
            .Where(o => o.UserId == userId)
            .ToListAsync();

        return orders.Select(ToResponse).ToList();
    }

    public async Task<OrderResponse> GetByIdAsync(Guid userId, Guid id)
    {
        var order = await FindOwnedOrder(userId, id);

        if (order is null)
        {
            throw new NotFoundException(nameof(Order), id);
        }

        return ToResponse(order);
    }

    public async Task<OrderResponse> UpdateAsync(Guid userId, Guid id, UpdateOrderRequest request)
    {
        var order = await FindOwnedOrder(userId, id);

        if (order is null)
        {
            throw new NotFoundException(nameof(Order), id);
        }

        var customer = await FindOwnedActiveCustomer(userId, request.CustomerId);

        if (customer is null)
        {
            throw new NotFoundException(nameof(Customer), request.CustomerId);
        }

        var productsById = await LoadOwnedActiveProducts(userId, request.Items);

        if (productsById is null)
        {
            throw new NotFoundException("One or more products", string.Join(",", request.Items.Select(i => i.ProductId)));
        }

        order.CustomerId = customer.Id;
        order.DueDate = request.DueDate;
        order.Discount = request.Discount;
        order.Notes = request.Notes;

        // Managed directly through the DbSet (not via the order.OrderItems navigation setter) -
        // replacing a required collection navigation by reassigning it confuses EF's change tracker
        // into generating an UPDATE against the row it just DELETEd instead of an INSERT.
        dbContext.OrderItems.RemoveRange(order.OrderItems);
        var newItems = request.Items.Select(i => new OrderItem
        {
            OrderId = order.Id,
            ProductId = i.ProductId,
            Product = productsById[i.ProductId],
            Quantity = i.Quantity,
            UnitPrice = productsById[i.ProductId].SellPrice
        }).ToList();
        dbContext.OrderItems.AddRange(newItems);

        // Validated against newItems directly, not order.Subtotal - assigning to the OrderItems
        // navigation before SaveChanges is what caused the earlier EF change-tracker bug, so it's
        // still only set on the in-memory object after the save below, for the response mapping.
        ValidateCashDiscount(newItems, request.Discount, request.CashDiscount);
        order.CashDiscount = request.CashDiscount;

        await dbContext.SaveChangesAsync();

        order.OrderItems = newItems;

        order.Customer = customer;
        return ToResponse(order);
    }

    public async Task DeleteAsync(Guid userId, Guid id)
    {
        var order = await dbContext.Orders.FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

        if (order is null)
        {
            throw new NotFoundException(nameof(Order), id);
        }

        dbContext.Orders.Remove(order);
        await dbContext.SaveChangesAsync();
    }

    public async Task<OrderResponse> RecordPaymentAsync(Guid userId, Guid id, RecordPaymentRequest request)
    {
        var order = await FindOwnedOrder(userId, id);

        if (order is null)
        {
            throw new NotFoundException(nameof(Order), id);
        }

        if (request.Amount > order.RemainingBalance + AmountTolerance)
        {
            throw new BadRequestException($"Payment amount ({request.Amount}) exceeds the remaining balance ({order.RemainingBalance}).");
        }

        order.PaidAmount += request.Amount;
        await dbContext.SaveChangesAsync();

        return ToResponse(order);
    }

    public async Task<OrderSummaryResponse> GetSummaryAsync(Guid userId, SummaryPeriod period)
    {
        var cutoff = period switch
        {
            SummaryPeriod.Week => DateTime.UtcNow.AddDays(-7),
            SummaryPeriod.Month => DateTime.UtcNow.AddDays(-30),
            SummaryPeriod.Year => DateTime.UtcNow.AddDays(-365),
            _ => (DateTime?)null
        };

        var query = dbContext.Orders.Where(o => o.UserId == userId);

        if (cutoff is not null)
        {
            query = query.Where(o => o.CreatedAt >= cutoff);
        }

        var orders = await query.ToListAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var summary = new OrderSummaryResponse
        {
            Period = period.ToString(),
            Count = orders.Count,
            TotalAmount = orders.Sum(o => o.Total),
            TotalPaid = orders.Sum(o => o.PaidAmount),
            TotalOutstanding = orders.Sum(o => o.RemainingBalance)
        };

        foreach (var order in orders)
        {
            switch (ComputeStatus(order, today))
            {
                case "Paid":
                    summary.PaidCount++;
                    break;
                case "Overdue":
                    summary.OverdueCount++;
                    break;
                case "PartiallyPaid":
                    summary.PartiallyPaidCount++;
                    break;
                default:
                    summary.SentCount++;
                    break;
            }
        }

        return summary;
    }

    internal static void ValidateCashDiscount(List<OrderItem> items, decimal discountPercent, decimal cashDiscount)
    {
        var subtotal = items.Sum(i => i.TotalPrice);
        var discountAmount = subtotal * (discountPercent / 100m);
        var payableAfterPercentageDiscount = subtotal - discountAmount;

        if (cashDiscount > payableAfterPercentageDiscount)
        {
            throw new BadRequestException(
                $"Cash discount ({cashDiscount}) exceeds the payable amount ({payableAfterPercentageDiscount}).");
        }
    }

    internal static string ComputeStatus(Order order, DateOnly today)
    {
        if (order.RemainingBalance <= AmountTolerance)
        {
            return "Paid";
        }

        if (order.DueDate < today)
        {
            return "Overdue";
        }

        if (order.PaidAmount > 0)
        {
            return "PartiallyPaid";
        }

        return "Sent";
    }

    private async Task<Customer?> FindOwnedActiveCustomer(Guid userId, Guid customerId) =>
        await dbContext.Customers.FirstOrDefaultAsync(c => c.Id == customerId && c.UserId == userId && c.IsActive);

    private async Task<Dictionary<Guid, Product>?> LoadOwnedActiveProducts(Guid userId, List<OrderItemRequest> items)
    {
        var productIds = items.Select(i => i.ProductId).Distinct().ToList();

        var products = await dbContext.Products
            .Where(p => productIds.Contains(p.Id) && p.UserId == userId && p.IsActive)
            .ToListAsync();

        return products.Count == productIds.Count ? products.ToDictionary(p => p.Id) : null;
    }

    private async Task<Order?> FindOwnedOrder(Guid userId, Guid id) =>
        await dbContext.Orders.Include(o => o.Customer).FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

    private async Task<string> GenerateInvoiceNumberAsync(Guid userId)
    {
        // A running per-user counter, not a COUNT of existing orders - orders can now be permanently
        // deleted, and counting would reissue an already-used invoice number as soon as any order
        // was removed from the middle or end of the sequence, colliding with the unique index.
        // The sequence resets to 1 at the start of each calendar year (INV/{year}/0001) so a yearly
        // data audit can work off a clean per-year range instead of one lifetime-long sequence.
        var user = await dbContext.Users.FirstAsync(u => u.Id == userId);
        var currentYear = DateTime.UtcNow.Year;
        if (user.NextInvoiceNumberYear != currentYear)
        {
            user.NextInvoiceNumberYear = currentYear;
            user.NextInvoiceNumber = 1;
        }
        var invoiceNumber = $"INV/{currentYear}/{user.NextInvoiceNumber:D4}";
        user.NextInvoiceNumber++;
        return invoiceNumber;
    }

    internal static OrderResponse ToResponse(Order order) => new()
    {
        Id = order.Id,
        InvoiceNumber = order.InvoiceNumber,
        Status = ComputeStatus(order, DateOnly.FromDateTime(DateTime.UtcNow)),
        CustomerId = order.CustomerId,
        CustomerName = order.Customer.Name,
        CustomerPhoneNumber = order.Customer.PhoneNumber,
        CustomerStreet = order.Customer.Street,
        CustomerCity = order.Customer.City,
        CreatedAt = order.CreatedAt,
        UpdatedAt = order.UpdatedAt,
        DueDate = order.DueDate,
        Discount = order.Discount,
        CashDiscount = order.CashDiscount,
        Notes = order.Notes,
        Subtotal = order.Subtotal,
        DiscountAmount = order.DiscountAmount,
        Total = order.Total,
        PaidAmount = order.PaidAmount,
        RemainingBalance = order.RemainingBalance,
        Items = order.OrderItems.Select(oi => new OrderItemResponse
        {
            ProductId = oi.ProductId,
            ProductName = oi.Product.Name,
            ProductDescription = oi.Product.Description,
            ProductImageUrl = oi.Product.ImageUrl,
            Quantity = oi.Quantity,
            UnitPrice = oi.UnitPrice,
            TotalPrice = oi.TotalPrice
        }).ToList()
    };
}
