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


    public async Task<OrderResponse> CreateAsync(Guid userId, CreateOrderRequest request, Guid? createdByRepId = null)
    {
        var customer = await FindOwnedActiveCustomer(userId, request.CustomerId, createdByRepId);

        if (customer is null)
        {
            throw new NotFoundException(nameof(Customer), request.CustomerId);
        }

        var productsById = await LoadOwnedActiveProducts(userId, request.Items, createdByRepId);

        if (productsById is null)
        {
            throw new NotFoundException("One or more products", string.Join(",", request.Items.Select(i => i.ProductId)));
        }

        var order = new Order
        {
            InvoiceNumber = await GenerateInvoiceNumberAsync(userId),
            CustomerId = customer.Id,
            UserId = userId,
            CreatedByRepId = createdByRepId,
            DueDate = request.DueDate,
            Discount = request.Discount,
            Notes = request.Notes,
            OrderItems = request.Items.Select(i => new OrderItem
            {
                ProductId = i.ProductId,
                Product = productsById[i.ProductId],
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice ?? productsById[i.ProductId].SellPrice
            }).ToList()
        };

        ValidateCashDiscount(order.OrderItems, request.Discount, request.CashDiscount);
        order.CashDiscount = request.CashDiscount;

        foreach (var lineItem in request.Items)
        {
            AdjustStock(productsById[lineItem.ProductId], -lineItem.Quantity);
        }

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        order.Customer = customer;
        return ToResponse(order);
    }

    public async Task<List<OrderResponse>> GetAllAsync(Guid userId, Guid? scopeToRepId = null)
    {
        var orders = await dbContext.Orders
            .Include(o => o.Customer)
            .Where(o => o.UserId == userId && (scopeToRepId == null || o.CreatedByRepId == scopeToRepId))
            .ToListAsync();

        return orders.Select(ToResponse).ToList();
    }

    // Newest-first, matching the order the invoice list has always shown -
    // a stable sort is required for Skip/Take to mean the same "page" twice.
    public async Task<List<OrderResponse>> GetPagedAsync(Guid userId, int skip, int take, Guid? scopeToRepId = null)
    {
        var orders = await dbContext.Orders
            .Include(o => o.Customer)
            .Where(o => o.UserId == userId && (scopeToRepId == null || o.CreatedByRepId == scopeToRepId))
            .OrderByDescending(o => o.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return orders.Select(ToResponse).ToList();
    }

    public async Task<OrderResponse> GetByIdAsync(Guid userId, Guid id, Guid? scopeToRepId = null)
    {
        var order = await FindOwnedOrder(userId, id, scopeToRepId);

        if (order is null)
        {
            throw new NotFoundException(nameof(Order), id);
        }

        return ToResponse(order);
    }

    public async Task<OrderResponse> UpdateAsync(Guid userId, Guid id, UpdateOrderRequest request, Guid? scopeToRepId = null)
    {
        var order = await FindOwnedOrder(userId, id, scopeToRepId);

        if (order is null)
        {
            throw new NotFoundException(nameof(Order), id);
        }

        if (order.IsReturned)
        {
            throw new BadRequestException("Cannot edit an invoice that has been marked as returned.");
        }

        if (ComputeStatus(order, DateOnly.FromDateTime(DateTime.UtcNow)) == "Paid")
        {
            throw new BadRequestException("Cannot edit an invoice that has already been paid in full.");
        }

        var customer = await FindOwnedActiveCustomer(userId, request.CustomerId, scopeToRepId);

        if (customer is null)
        {
            throw new NotFoundException(nameof(Customer), request.CustomerId);
        }

        var productsById = await LoadOwnedActiveProducts(userId, request.Items, scopeToRepId);

        if (productsById is null)
        {
            throw new NotFoundException("One or more products", string.Join(",", request.Items.Select(i => i.ProductId)));
        }

        var isEdit = order.CustomerId != customer.Id
            || order.Discount != request.Discount
            || order.CashDiscount != request.CashDiscount
            || !OrderItemsMatch(order.OrderItems, request.Items.Select(i =>
                (i.ProductId, i.Quantity, i.UnitPrice ?? productsById[i.ProductId].SellPrice)));

        order.CustomerId = customer.Id;
        order.DueDate = request.DueDate;
        order.Discount = request.Discount;
        order.Notes = request.Notes;

        if (isEdit)
        {
            order.InvoiceNumber = await GenerateInvoiceNumberAsync(userId);
            order.IsEdited = true;
        }

        // Restore stock for whatever this order was previously charging
        // before removing those line items below, then decrement for the
        // new ones - a product common to both nets out correctly since
        // order.OrderItems[].Product and productsById are the same tracked
        // instances within this DbContext.
        foreach (var oldItem in order.OrderItems)
        {
            AdjustStock(oldItem.Product, oldItem.Quantity);
        }

        // Managed directly through the DbSet (not via the order.OrderItems navigation setter) -
        // replacing a required collection navigation by reassigning it confuses EF's change tracker
        // into generating an UPDATE against the row it just DELETEd instead of an INSERT.
        var previousItems = order.OrderItems.ToList();
        dbContext.OrderItems.RemoveRange(order.OrderItems);
        var newItems = request.Items.Select(i =>
        {
            var unitPrice = i.UnitPrice ?? productsById[i.ProductId].SellPrice;
            return new OrderItem
            {
                OrderId = order.Id,
                ProductId = i.ProductId,
                Product = productsById[i.ProductId],
                Quantity = i.Quantity,
                UnitPrice = unitPrice,
                IsEdited = ResolveItemIsEdited(previousItems, i.ProductId, i.Quantity, unitPrice)
            };
        }).ToList();
        dbContext.OrderItems.AddRange(newItems);

        foreach (var lineItem in request.Items)
        {
            AdjustStock(productsById[lineItem.ProductId], -lineItem.Quantity);
        }

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

    public async Task DeleteAsync(Guid userId, Guid id, Guid? scopeToRepId = null)
    {
        var order = await dbContext.Orders.FirstOrDefaultAsync(o =>
            o.Id == id && o.UserId == userId && (scopeToRepId == null || o.CreatedByRepId == scopeToRepId));

        if (order is null)
        {
            throw new NotFoundException(nameof(Order), id);
        }

        // Skip if already returned - ReturnAsync already restored this
        // stock once; doing it again here would double-count it.
        if (!order.IsReturned)
        {
            // OrderItems (and their Product) are auto-included - see
            // OrderConfiguration/ItemConfiguration.
            foreach (var lineItem in order.OrderItems)
            {
                AdjustStock(lineItem.Product, lineItem.Quantity);
            }
        }

        dbContext.Orders.Remove(order);
        await dbContext.SaveChangesAsync();
    }

    // Marks an invoice as returned instead of deleting it - permanent (no
    // "un-return"), restores every line's stock quantity exactly once, and
    // from that point on RemainingBalance/ComputeStatus/report sales sums
    // all treat it as returned automatically. PaidAmount is deliberately
    // never touched here - see Order.RemainingBalance for why.
    public async Task<OrderResponse> ReturnAsync(Guid userId, Guid id, Guid? scopeToRepId = null)
    {
        var order = await FindOwnedOrder(userId, id, scopeToRepId);

        if (order is null)
        {
            throw new NotFoundException(nameof(Order), id);
        }

        if (order.IsReturned)
        {
            throw new ConflictException("This invoice has already been marked as returned.");
        }

        foreach (var item in order.OrderItems)
        {
            AdjustStock(item.Product, item.Quantity);
        }

        order.IsReturned = true;
        await dbContext.SaveChangesAsync();

        return ToResponse(order);
    }

    public async Task<OrderResponse> RecordPaymentAsync(Guid userId, Guid id, RecordPaymentRequest request, Guid? scopeToRepId = null)
    {
        var order = await FindOwnedOrder(userId, id, scopeToRepId);

        if (order is null)
        {
            throw new NotFoundException(nameof(Order), id);
        }

        if (order.IsReturned)
        {
            throw new BadRequestException("Cannot record a payment on a returned invoice.");
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

    // Order-independent: what matters is which products and quantities are
    // on the invoice, not the order they happen to be listed in. Shared by
    // OrderService.UpdateAsync and SyncService.PushOrderAsync so both
    // decide "was this invoice actually edited" the same way.
    internal static bool OrderItemsMatch(
        ICollection<OrderItem> existing,
        IEnumerable<(Guid ProductId, int Quantity, decimal UnitPrice)> incoming)
    {
        var existingByProduct = existing.ToDictionary(i => i.ProductId, i => (i.Quantity, i.UnitPrice));
        var incomingByProduct = incoming.ToDictionary(i => i.ProductId, i => (i.Quantity, i.UnitPrice));
        if (existingByProduct.Count != incomingByProduct.Count)
        {
            return false;
        }

        return existingByProduct.All(pair =>
            incomingByProduct.TryGetValue(pair.Key, out var line) && line == pair.Value);
    }

    // Per-item mirror of the check above, for the small green "معدّل" marker
    // on an individual line rather than the whole invoice: a product with no
    // match in the prior item set is a newly-added line (edited), a matched
    // one with a different quantity or price was tampered with (edited), and
    // a matched, unchanged one carries over whatever its own IsEdited flag
    // already was - so a line's audit trail survives a later save that
    // doesn't touch it again. Shared by OrderService.UpdateAsync and
    // SyncService.PushOrderAsync, same as OrderItemsMatch above.
    internal static bool ResolveItemIsEdited(
        ICollection<OrderItem> existing,
        Guid productId,
        int quantity,
        decimal unitPrice)
    {
        var previous = existing.FirstOrDefault(i => i.ProductId == productId);
        if (previous is null)
        {
            return true;
        }

        return previous.Quantity != quantity || previous.UnitPrice != unitPrice || previous.IsEdited;
    }

    // Never validated/clamped here - see Product.StockQuantity: a sale must
    // never be blocked by insufficient stock, negative is allowed.
    private static void AdjustStock(Product product, int delta)
    {
        if (product.StockQuantity is { } stock)
        {
            product.StockQuantity = stock + delta;
        }
    }

    internal static string ComputeStatus(Order order, DateOnly today)
    {
        if (order.IsReturned)
        {
            return "Returned";
        }

        if (order.RemainingBalance <= AmountTolerance)
        {
            return "Paid";
        }

        // A null due date (the user has due dates turned off) can never be
        // overdue - there's nothing to be late against.
        if (order.DueDate is { } dueDate && dueDate < today)
        {
            return "Overdue";
        }

        if (order.PaidAmount > 0)
        {
            return "PartiallyPaid";
        }

        return "Sent";
    }

    // scopeToRepId null means "the owner, any of their customers" - non-null
    // resolves through that Rep's own CustomerAccessMode - see
    // CustomerService.FindOwnedActiveCustomer for the identical rule and the
    // full rundown of what Own/All/Restricted each mean.
    private async Task<Customer?> FindOwnedActiveCustomer(Guid userId, Guid customerId, Guid? scopeToRepId)
    {
        var customer = await dbContext.Customers.FirstOrDefaultAsync(c =>
            c.Id == customerId && c.UserId == userId && c.IsActive);
        if (customer is null) return null;

        if (scopeToRepId is { } repId)
        {
            var rep = await dbContext.Reps.FirstOrDefaultAsync(r => r.Id == repId);
            var visible = rep is null || rep.CustomerAccessMode switch
            {
                CustomerAccessMode.All => true,
                CustomerAccessMode.Restricted => customer.CreatedByRepId == repId
                    || await dbContext.RepCustomerAccesses.AnyAsync(a => a.RepId == repId && a.CustomerId == customerId),
                _ => customer.CreatedByRepId == repId,
            };
            if (!visible) return null;
        }

        return customer;
    }

    // scopeToRepId, when it names a Restricted rep, additionally requires
    // every product to be in that rep's own RepProductAccess grants - a
    // missing grant makes this indistinguishable from the product simply
    // not existing (same null return, same NotFoundException at the call
    // site), which is the actual backend enforcement: a restricted rep
    // cannot invoice a product it can't see, even by calling this directly
    // with a known id.
    private async Task<Dictionary<Guid, Product>?> LoadOwnedActiveProducts(
        Guid userId, List<OrderItemRequest> items, Guid? scopeToRepId)
    {
        var productIds = items.Select(i => i.ProductId).Distinct().ToList();

        var query = dbContext.Products.Where(p => productIds.Contains(p.Id) && p.UserId == userId && p.IsActive);

        if (scopeToRepId is { } repId)
        {
            var rep = await dbContext.Reps.FirstOrDefaultAsync(r => r.Id == repId);
            if (rep is { ProductAccessMode: AccessMode.Restricted })
            {
                var allowedIds = dbContext.RepProductAccesses.Where(a => a.RepId == repId).Select(a => a.ProductId);
                query = query.Where(p => allowedIds.Contains(p.Id));
            }
        }

        var products = await query.ToListAsync();

        return products.Count == productIds.Count ? products.ToDictionary(p => p.Id) : null;
    }

    private async Task<Order?> FindOwnedOrder(Guid userId, Guid id, Guid? scopeToRepId) =>
        await dbContext.Orders.Include(o => o.Customer).FirstOrDefaultAsync(o =>
            o.Id == id && o.UserId == userId && (scopeToRepId == null || o.CreatedByRepId == scopeToRepId));

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
        CustomerPhoneNumber = order.Customer.PhoneNumber ?? string.Empty,
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
        CoveredByReceipt = order.CoveredByReceipt,
        IsEdited = order.IsEdited,
        IsReturned = order.IsReturned,
        Items = order.OrderItems.Select(oi => new OrderItemResponse
        {
            ProductId = oi.ProductId,
            ProductName = oi.Product.Name,
            ProductDescription = oi.Product.Description,
            ProductImageUrl = oi.Product.ImageUrl,
            Quantity = oi.Quantity,
            UnitPrice = oi.UnitPrice,
            TotalPrice = oi.TotalPrice,
            IsEdited = oi.IsEdited
        }).ToList()
    };
}
