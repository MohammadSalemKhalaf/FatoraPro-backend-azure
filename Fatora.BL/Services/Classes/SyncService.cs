using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;

namespace Fatora.BL.Services.Classes;

public class SyncService(AppDbContext dbContext) : ISyncService
{
    public async Task<SyncPushResponse> PushAsync(Guid userId, SyncPushRequest request)
    {
        // Preserve the device's real edit timestamps (possibly recorded hours ago while offline)
        // instead of stamping "now" on arrival - last-write-wins depends on this being accurate.
        dbContext.SuppressAutoTimestamps = true;

        var response = new SyncPushResponse();

        foreach (var item in request.Customers)
        {
            response.Customers.Add(await PushCustomerAsync(userId, item));
        }

        foreach (var item in request.Products)
        {
            response.Products.Add(await PushProductAsync(userId, item));
        }

        // Orders may reference customers/products from this same batch, so they must be
        // processed (and individually saved) after the two loops above complete.
        foreach (var item in request.Orders)
        {
            response.Orders.Add(await PushOrderAsync(userId, item));
        }

        foreach (var orderId in request.DeletedOrderIds)
        {
            response.DeletedOrders.Add(await DeleteOrderAsync(userId, orderId));
        }

        return response;
    }

    public async Task<SyncPullResponse> PullAsync(Guid userId, DateTime since)
    {
        var serverTime = DateTime.UtcNow;

        var customers = await dbContext.Customers
            .Where(c => c.UserId == userId && c.UpdatedAt > since)
            .ToListAsync();

        var products = await dbContext.Products
            .Where(p => p.UserId == userId && p.UpdatedAt > since)
            .ToListAsync();

        var orders = await dbContext.Orders
            .Include(o => o.Customer)
            .Where(o => o.UserId == userId && o.UpdatedAt > since)
            .ToListAsync();

        return new SyncPullResponse
        {
            ServerTime = serverTime,
            Customers = customers.Select(CustomerService.ToResponse).ToList(),
            Products = products.Select(ProductService.ToResponse).ToList(),
            Orders = orders.Select(OrderService.ToResponse).ToList()
        };
    }

    private async Task<SyncItemResult> PushCustomerAsync(Guid userId, CustomerSyncItem item)
    {
        try
        {
            var existing = await dbContext.Customers.FirstOrDefaultAsync(c => c.Id == item.Id && c.UserId == userId);

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

    private async Task<SyncItemResult> PushOrderAsync(Guid userId, OrderSyncItem item)
    {
        try
        {
            var existing = await dbContext.Orders.Include(o => o.Customer).FirstOrDefaultAsync(o => o.Id == item.Id && o.UserId == userId);

            if (existing is null)
            {
                var customer = await dbContext.Customers.FirstOrDefaultAsync(c => c.Id == item.CustomerId && c.UserId == userId);

                if (customer is null)
                {
                    return new SyncItemResult(item.Id, "Rejected", "Customer not found.");
                }

                var productsById = await LoadOwnedProducts(userId, item.Items);

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
                    DueDate = item.DueDate,
                    Discount = item.Discount,
                    Notes = item.Notes,
                    PaidAmount = item.PaidAmount,
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

            var newCustomer = await dbContext.Customers.FirstOrDefaultAsync(c => c.Id == item.CustomerId && c.UserId == userId);

            if (newCustomer is null)
            {
                return new SyncItemResult(item.Id, "Rejected", "Customer not found.");
            }

            var newProductsById = await LoadOwnedProducts(userId, item.Items);

            if (newProductsById is null)
            {
                return new SyncItemResult(item.Id, "Rejected", "One or more products not found.");
            }

            existing.CustomerId = newCustomer.Id;
            existing.DueDate = item.DueDate;
            existing.Discount = item.Discount;
            existing.Notes = item.Notes;
            existing.PaidAmount = item.PaidAmount;
            existing.UpdatedAt = item.UpdatedAt;

            // Managed directly through the DbSet (not via the existing.OrderItems navigation setter) -
            // replacing a required collection navigation by reassigning it confuses EF's change tracker
            // into generating an UPDATE against the row it just DELETEd instead of an INSERT.
            dbContext.OrderItems.RemoveRange(existing.OrderItems);
            var newItems = item.Items.Select(i => new OrderItem
            {
                OrderId = existing.Id,
                ProductId = i.ProductId,
                Product = newProductsById[i.ProductId],
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList();
            dbContext.OrderItems.AddRange(newItems);

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

    private async Task<SyncItemResult> DeleteOrderAsync(Guid userId, Guid orderId)
    {
        try
        {
            var order = await dbContext.Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId);

            if (order is not null)
            {
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

    private async Task<Dictionary<Guid, Product>?> LoadOwnedProducts(Guid userId, List<OrderItemSyncItem> items)
    {
        var productIds = items.Select(i => i.ProductId).Distinct().ToList();

        var products = await dbContext.Products
            .Where(p => productIds.Contains(p.Id) && p.UserId == userId)
            .ToListAsync();

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
