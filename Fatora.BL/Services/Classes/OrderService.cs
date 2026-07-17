using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Fatora.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fatora.BL.Services.Classes;

public class OrderService(AppDbContext dbContext) : IOrderService
{
    public async Task<OrderResponse?> CreateAsync(Guid userId, CreateOrderRequest request)
    {
        var customer = await FindOwnedActiveCustomer(userId, request.CustomerId);

        if (customer is null)
        {
            throw new NotFoundException($"Customer with this {userId} was not found");;
        }

        var productsById = await LoadOwnedActiveProducts(userId, request.Items);

        if (productsById is null)
        {
             throw new NotFoundException($"Products for this user:{userId}  was not found");;
;
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

    public async Task<OrderResponse?> GetByIdAsync(Guid userId, Guid id)
    {
        var order = await FindOwnedOrder(userId, id);
        return order is null ? throw new NotFoundException($"Customer with this {id} was not found")
 : ToResponse(order);
    }

    public async Task<OrderResponse?> UpdateAsync(Guid userId, Guid id, UpdateOrderRequest request)
    {
        var order = await FindOwnedOrder(userId, id);

        if (order is null)
        {
            throw new NotFoundException($"Order with this {id} was not found");;
;
        }

        var customer = await FindOwnedActiveCustomer(userId, request.CustomerId);

        if (customer is null)
        {
            throw new NotFoundException($"Customer with this {id} was not found");
;
        }

        var productsById = await LoadOwnedActiveProducts(userId, request.Items);

        if (productsById is null)
        {
            throw new NotFoundException($"product with this {id} was not found");;
;
        }

        order.CustomerId = customer.Id;
        order.DueDate = request.DueDate;
        order.Discount = request.Discount;
        order.Notes = request.Notes;

        dbContext.OrderItems.RemoveRange(order.OrderItems);
        order.OrderItems = request.Items.Select(i => new OrderItem
        {
            ProductId = i.ProductId,
            Quantity = i.Quantity,
            UnitPrice = productsById[i.ProductId].SellPrice
        }).ToList();

        await dbContext.SaveChangesAsync();

        order.Customer = customer;
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
            if (order.RemainingBalance <= 0)
            {
                summary.PaidCount++;
            }
            else if (order.DueDate < today)
            {
                summary.OverdueCount++;
            }
            else if (order.PaidAmount > 0)
            {
                summary.PartiallyPaidCount++;
            }
            else
            {
                summary.UnpaidCount++;
            }
        }

        return summary;
    }

    private async Task<Customer?> FindOwnedActiveCustomer(Guid userId, int customerId) =>
        await dbContext.Customers.FirstOrDefaultAsync(c => c.Id == customerId && c.UserId == userId && c.IsActive);

    private async Task<Dictionary<int, Product>?> LoadOwnedActiveProducts(Guid userId, List<OrderItemRequest> items)
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
        var count = await dbContext.Orders.CountAsync(o => o.UserId == userId);
        return $"INV-{count + 1:D4}";
    }

    private static OrderResponse ToResponse(Order order) => new()
    {
        Id = order.Id,
        InvoiceNumber = order.InvoiceNumber,
        CustomerId = order.CustomerId,
        CustomerName = order.Customer.Name,
        CreatedAt = order.CreatedAt,
        DueDate = order.DueDate,
        Discount = order.Discount,
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
        }).ToList(),
        Payments = order.Payments.Select(p => new PaymentResponse
        {
            Id = p.Id,
            Amount = p.Amount,
            PaidAt = p.PaidAt,
            Notes = p.Notes
        }).ToList()
    };
}
