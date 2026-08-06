using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;

namespace Fatora.BL.Services.Classes;

public class SyncService(AppDbContext dbContext) : ISyncService
{
    public async Task<SyncPushResponse> PushAsync(Guid userId, SyncPushRequest request, Guid? scopeToRepId = null)
    {
        // Preserve the device's real edit timestamps (possibly recorded hours ago while offline)
        // instead of stamping "now" on arrival - last-write-wins depends on this being accurate.
        dbContext.SuppressAutoTimestamps = true;

        var response = new SyncPushResponse();

        foreach (var item in request.Customers)
        {
            response.Customers.Add(await PushCustomerAsync(userId, item, scopeToRepId));
        }

        foreach (var item in request.Products)
        {
            response.Products.Add(await PushProductAsync(userId, item));
        }

        // Orders may reference customers/products from this same batch, so they must be
        // processed (and individually saved) after the two loops above complete.
        foreach (var item in request.Orders)
        {
            response.Orders.Add(await PushOrderAsync(userId, item, scopeToRepId));
        }

        foreach (var item in request.Receipts)
        {
            response.Receipts.Add(await PushReceiptAsync(userId, item, scopeToRepId));
        }

        foreach (var orderId in request.DeletedOrderIds)
        {
            response.DeletedOrders.Add(await DeleteOrderAsync(userId, orderId, scopeToRepId));
        }

        return response;
    }

    public async Task<SyncPullResponse> PullAsync(Guid userId, DateTime since, Guid? scopeToRepId = null)
    {
        var serverTime = DateTime.UtcNow;

        // Products are never Rep-scoped in R1 (every Rep sees the full
        // catalog by default - see Rep.ProductAccessMode) - Customers/
        // Orders/Receipts are, so a Rep's own device only ever pulls down
        // its own slice, never another Rep's or the owner's directly-
        // created records.
        var customers = await dbContext.Customers
            .Where(c => c.UserId == userId && c.UpdatedAt > since
                && (scopeToRepId == null || c.CreatedByRepId == scopeToRepId))
            .ToListAsync();

        var products = await dbContext.Products
            .Where(p => p.UserId == userId && p.UpdatedAt > since)
            .ToListAsync();

        var orders = await dbContext.Orders
            .Include(o => o.Customer)
            .Where(o => o.UserId == userId && o.UpdatedAt > since
                && (scopeToRepId == null || o.CreatedByRepId == scopeToRepId))
            .ToListAsync();

        var receipts = await dbContext.Receipts
            .Where(r => r.UserId == userId && r.UpdatedAt > since
                && (scopeToRepId == null || r.CreatedByRepId == scopeToRepId))
            .ToListAsync();

        return new SyncPullResponse
        {
            ServerTime = serverTime,
            Customers = customers.Select(CustomerService.ToResponse).ToList(),
            Products = products.Select(ProductService.ToResponse).ToList(),
            Orders = orders.Select(OrderService.ToResponse).ToList(),
            Receipts = receipts.Select(ReceiptService.ToResponse).ToList()
        };
    }

    private async Task<SyncItemResult> PushCustomerAsync(Guid userId, CustomerSyncItem item, Guid? scopeToRepId)
    {
        try
        {
            var existing = await dbContext.Customers.FirstOrDefaultAsync(c =>
                c.Id == item.Id && c.UserId == userId
                && (scopeToRepId == null || c.CreatedByRepId == scopeToRepId));

            if (existing is null)
            {
                dbContext.Customers.Add(new Customer
                {
                    Id = item.Id,
                    Name = item.Name,
                    StoreName = item.StoreName,
                    PhoneNumber = item.PhoneNumber,
                    Street = item.Street,
                    City = item.City,
                    IsActive = item.IsActive,
                    UserId = userId,
                    CreatedByRepId = scopeToRepId,
                    CreatedAt = item.UpdatedAt,
                    UpdatedAt = item.UpdatedAt
                });

                await dbContext.SaveChangesAsync();
                return new SyncItemResult(item.Id, "Applied");
            }

            if (item.UpdatedAt <= existing.UpdatedAt)
            {
                return new SyncItemResult(item.Id, "Conflict", "Server has a newer or equal version; pull to reconcile.");
            }

            existing.Name = item.Name;
            existing.StoreName = item.StoreName;
            existing.PhoneNumber = item.PhoneNumber;
            existing.Street = item.Street;
            existing.City = item.City;
            existing.IsActive = item.IsActive;
            existing.UpdatedAt = item.UpdatedAt;

            await dbContext.SaveChangesAsync();
            return new SyncItemResult(item.Id, "Applied");
        }
        catch (Exception ex)
        {
            return new SyncItemResult(item.Id, "Rejected", ex.Message);
        }
    }

    private async Task<SyncItemResult> PushProductAsync(Guid userId, ProductSyncItem item)
    {
        try
        {
            var existing = await dbContext.Products.FirstOrDefaultAsync(p => p.Id == item.Id && p.UserId == userId);

            if (existing is null)
            {
                dbContext.Products.Add(new Product
                {
                    Id = item.Id,
                    Name = item.Name,
                    Description = item.Description,
                    ImageUrl = item.ImageUrl,
                    Barcode = item.Barcode,
                    PurchasePrice = item.PurchasePrice,
                    SellPrice = item.SellPrice,
                    StockQuantity = item.StockQuantity,
                    IsActive = item.IsActive,
                    UserId = userId,
                    CreatedAt = item.UpdatedAt,
                    UpdatedAt = item.UpdatedAt
                });

                await dbContext.SaveChangesAsync();
                return new SyncItemResult(item.Id, "Applied");
            }

            if (item.UpdatedAt <= existing.UpdatedAt)
            {
                return new SyncItemResult(item.Id, "Conflict", "Server has a newer or equal version; pull to reconcile.");
            }

            existing.Name = item.Name;
            existing.Description = item.Description;
            existing.ImageUrl = item.ImageUrl;
            existing.Barcode = item.Barcode;
            existing.PurchasePrice = item.PurchasePrice;
            existing.SellPrice = item.SellPrice;
            existing.StockQuantity = item.StockQuantity;
            existing.IsActive = item.IsActive;
            existing.UpdatedAt = item.UpdatedAt;

            await dbContext.SaveChangesAsync();
            return new SyncItemResult(item.Id, "Applied");
        }
        catch (Exception ex)
        {
            return new SyncItemResult(item.Id, "Rejected", ex.Message);
        }
    }

    private async Task<SyncItemResult> PushOrderAsync(Guid userId, OrderSyncItem item, Guid? scopeToRepId)
    {
        try
        {
            var existing = await dbContext.Orders.Include(o => o.Customer).FirstOrDefaultAsync(o =>
                o.Id == item.Id && o.UserId == userId
                && (scopeToRepId == null || o.CreatedByRepId == scopeToRepId));

            if (existing is null)
            {
                var customer = await dbContext.Customers.FirstOrDefaultAsync(c =>
                    c.Id == item.CustomerId && c.UserId == userId
                    && (scopeToRepId == null || c.CreatedByRepId == scopeToRepId));

                if (customer is null)
                {
                    return new SyncItemResult(item.Id, "Rejected", "Customer not found.");
                }

                var productsById = await LoadOwnedProducts(userId, item.Items, scopeToRepId);

                if (productsById is null)
                {
                    return new SyncItemResult(item.Id, "Rejected", "One or more products not found.");
                }

                var order = new Order
                {
                    Id = item.Id,
                    InvoiceNumber = await GenerateInvoiceNumberAsync(userId),
                    CustomerId = customer.Id,
                    UserId = userId,
                    CreatedByRepId = scopeToRepId,
                    DueDate = item.DueDate,
                    Discount = item.Discount,
                    Notes = item.Notes,
                    PaidAmount = item.PaidAmount,
                    CoveredByReceipt = item.CoveredByReceipt,
                    CreatedAt = item.UpdatedAt,
                    UpdatedAt = item.UpdatedAt,
                    OrderItems = item.Items.Select(i => new OrderItem
                    {
                        ProductId = i.ProductId,
                        Product = productsById[i.ProductId],
                        Quantity = i.Quantity,
                        UnitPrice = i.UnitPrice
                    }).ToList()
                };

                OrderService.ValidateCashDiscount(order.OrderItems, item.Discount, item.CashDiscount);
                order.CashDiscount = item.CashDiscount;

                foreach (var lineItem in item.Items)
                {
                    AdjustStock(productsById[lineItem.ProductId], -lineItem.Quantity);
                }

                dbContext.Orders.Add(order);
                await dbContext.SaveChangesAsync();
                return new SyncItemResult(item.Id, "Applied");
            }

            if (item.UpdatedAt <= existing.UpdatedAt)
            {
                return new SyncItemResult(item.Id, "Conflict", "Server has a newer or equal version; pull to reconcile.");
            }

            // Mirrors OrderService.UpdateAsync's same rule - the client is expected to block this
            // locally before it ever reaches here (see OrdersRepository.update), so this is a
            // backstop against a stale/offline edit that started before the order was marked paid.
            if (OrderService.ComputeStatus(existing, DateOnly.FromDateTime(DateTime.UtcNow)) == "Paid")
            {
                return new SyncItemResult(item.Id, "Rejected", "Cannot edit an invoice that has already been paid in full.");
            }

            var newCustomer = await dbContext.Customers.FirstOrDefaultAsync(c =>
                c.Id == item.CustomerId && c.UserId == userId
                && (scopeToRepId == null || c.CreatedByRepId == scopeToRepId));

            if (newCustomer is null)
            {
                return new SyncItemResult(item.Id, "Rejected", "Customer not found.");
            }

            var newProductsById = await LoadOwnedProducts(userId, item.Items, scopeToRepId);

            if (newProductsById is null)
            {
                return new SyncItemResult(item.Id, "Rejected", "One or more products not found.");
            }

            var isEdit = existing.CustomerId != newCustomer.Id
                || existing.Discount != item.Discount
                || existing.CashDiscount != item.CashDiscount
                || !OrderService.OrderItemsMatch(existing.OrderItems, item.Items.Select(i => (i.ProductId, i.Quantity, i.UnitPrice)));

            existing.CustomerId = newCustomer.Id;
            existing.DueDate = item.DueDate;
            existing.Discount = item.Discount;
            existing.Notes = item.Notes;
            existing.PaidAmount = item.PaidAmount;
            existing.CoveredByReceipt = item.CoveredByReceipt;
            existing.UpdatedAt = item.UpdatedAt;

            if (isEdit)
            {
                existing.InvoiceNumber = await GenerateInvoiceNumberAsync(userId);
                existing.IsEdited = true;
            }

            // Restore stock for whatever this order was previously charging
            // before removing those line items below, then decrement for
            // the new ones - a product common to both nets out correctly
            // since existing.OrderItems[].Product and newProductsById are
            // the same tracked instances within this DbContext.
            foreach (var oldItem in existing.OrderItems)
            {
                AdjustStock(oldItem.Product, oldItem.Quantity);
            }

            // Managed directly through the DbSet (not via the existing.OrderItems navigation setter) -
            // replacing a required collection navigation by reassigning it confuses EF's change tracker
            // into generating an UPDATE against the row it just DELETEd instead of an INSERT.
            var previousItems = existing.OrderItems.ToList();
            dbContext.OrderItems.RemoveRange(existing.OrderItems);
            var newItems = item.Items.Select(i => new OrderItem
            {
                OrderId = existing.Id,
                ProductId = i.ProductId,
                Product = newProductsById[i.ProductId],
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                IsEdited = OrderService.ResolveItemIsEdited(previousItems, i.ProductId, i.Quantity, i.UnitPrice)
            }).ToList();
            dbContext.OrderItems.AddRange(newItems);

            foreach (var lineItem in item.Items)
            {
                AdjustStock(newProductsById[lineItem.ProductId], -lineItem.Quantity);
            }

            OrderService.ValidateCashDiscount(newItems, item.Discount, item.CashDiscount);
            existing.CashDiscount = item.CashDiscount;

            await dbContext.SaveChangesAsync();
            return new SyncItemResult(item.Id, "Applied");
        }
        catch (Exception ex)
        {
            return new SyncItemResult(item.Id, "Rejected", ex.Message);
        }
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

    // Mirrors PushCustomerAsync: no dedicated REST endpoint exists for
    // receipts, they're created/updated/soft-deleted (IsActive = false)
    // purely through this sync path, same as customers.
    private async Task<SyncItemResult> PushReceiptAsync(Guid userId, ReceiptSyncItem item, Guid? scopeToRepId)
    {
        try
        {
            var existing = await dbContext.Receipts.FirstOrDefaultAsync(r =>
                r.Id == item.Id && r.UserId == userId
                && (scopeToRepId == null || r.CreatedByRepId == scopeToRepId));

            if (existing is null)
            {
                var customer = await dbContext.Customers.FirstOrDefaultAsync(c =>
                    c.Id == item.CustomerId && c.UserId == userId
                    && (scopeToRepId == null || c.CreatedByRepId == scopeToRepId));

                if (customer is null)
                {
                    return new SyncItemResult(item.Id, "Rejected", "Customer not found.");
                }

                dbContext.Receipts.Add(new Receipt
                {
                    Id = item.Id,
                    CustomerId = customer.Id,
                    Amount = item.Amount,
                    IsActive = item.IsActive,
                    UserId = userId,
                    CreatedByRepId = scopeToRepId,
                    CreatedAt = item.UpdatedAt,
                    UpdatedAt = item.UpdatedAt
                });

                await dbContext.SaveChangesAsync();
                return new SyncItemResult(item.Id, "Applied");
            }

            if (item.UpdatedAt <= existing.UpdatedAt)
            {
                return new SyncItemResult(item.Id, "Conflict", "Server has a newer or equal version; pull to reconcile.");
            }

            existing.Amount = item.Amount;
            existing.IsActive = item.IsActive;
            existing.UpdatedAt = item.UpdatedAt;

            await dbContext.SaveChangesAsync();
            return new SyncItemResult(item.Id, "Applied");
        }
        catch (Exception ex)
        {
            return new SyncItemResult(item.Id, "Rejected", ex.Message);
        }
    }

    private async Task<SyncItemResult> DeleteOrderAsync(Guid userId, Guid orderId, Guid? scopeToRepId)
    {
        try
        {
            var order = await dbContext.Orders.FirstOrDefaultAsync(o =>
                o.Id == orderId && o.UserId == userId
                && (scopeToRepId == null || o.CreatedByRepId == scopeToRepId));

            if (order is not null)
            {
                // OrderItems (and their Product) are auto-included - see
                // OrderConfiguration/ItemConfiguration.
                foreach (var lineItem in order.OrderItems)
                {
                    AdjustStock(lineItem.Product, lineItem.Quantity);
                }
                dbContext.Orders.Remove(order);
                await dbContext.SaveChangesAsync();
            }

            return new SyncItemResult(orderId, "Deleted");
        }
        catch (Exception ex)
        {
            return new SyncItemResult(orderId, "Rejected", ex.Message);
        }
    }

    // See OrderService.LoadOwnedActiveProducts - same rep-restriction
    // enforcement, mirrored here so a restricted rep can't route around it
    // through the sync push path instead of the plain REST one.
    private async Task<Dictionary<Guid, Product>?> LoadOwnedProducts(
        Guid userId, List<OrderItemSyncItem> items, Guid? scopeToRepId)
    {
        var productIds = items.Select(i => i.ProductId).Distinct().ToList();

        var query = dbContext.Products.Where(p => productIds.Contains(p.Id) && p.UserId == userId);

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
}
