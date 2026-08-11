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

        return response;
    }

    public async Task<SyncPullResponse> PullAsync(Guid userId, DateTime since, Guid? scopeToRepId = null)
    {
        var serverTime = DateTime.UtcNow;

        // Products are never Rep-scoped (every Rep sees the full catalog by
        // default - see Rep.ProductAccessMode). Orders/Receipts always pull
        // only what this Rep itself created. Customers depend on the Rep's
        // own CustomerAccessMode - see ApplyCustomerRepScopeAsync - so an
        // All/Restricted Rep's device also pulls down the customers it's
        // been given shared access to, not only the ones it created.
        var customersQuery = dbContext.Customers.Where(c => c.UserId == userId && c.UpdatedAt > since);
        customersQuery = await ApplyCustomerRepScopeAsync(customersQuery, scopeToRepId);
        var customers = await customersQuery.ToListAsync();

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
            var existing = await FindVisibleCustomerAsync(userId, item.Id, scopeToRepId);

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
                var customer = await FindVisibleCustomerAsync(userId, item.CustomerId, scopeToRepId);

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
                    IsReturned = item.IsReturned,
                    // Only reachable if this order was created and then
                    // deleted offline before its very first sync - a Rep
                    // can't reach this either way (delete is owner-only, see
                    // the dedicated IsDeleted branch below for the normal
                    // case where the order already exists server-side).
                    IsDeleted = scopeToRepId is null && item.IsDeleted,
                    CreatedAt = item.UpdatedAt,
                    UpdatedAt = item.UpdatedAt,
                    Latitude = item.Latitude,
                    Longitude = item.Longitude,
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

            // A push that only just marked this deleted (see
            // OrdersRepository.delete) is handled before every other gate
            // below - mirrors OrderService.DeleteAsync's own two checks
            // (owner-only, already-Returned) exactly, since this is the
            // second, offline-first way that same delete can reach the
            // server. No stock/customer/item reconciliation, no status
            // change, just the flag itself - see Order.IsDeleted for why it
            // deliberately has no other effect.
            if (item.IsDeleted && !existing.IsDeleted)
            {
                if (scopeToRepId is not null)
                {
                    return new SyncItemResult(item.Id, "Rejected", "Only the account owner can delete an invoice.");
                }

                if (!existing.IsReturned)
                {
                    return new SyncItemResult(item.Id, "Rejected", "Only a returned invoice can be deleted.");
                }

                existing.IsDeleted = true;
                existing.UpdatedAt = item.UpdatedAt;
                await dbContext.SaveChangesAsync();
                return new SyncItemResult(item.Id, "Applied");
            }

            // A push that only just marked this returned (see
            // OrdersRepository.returnOrder - nothing else about the order
            // changes) is handled entirely separately from a normal edit
            // below: no customer/item/discount reconciliation, just the
            // one-time stock restoration and the flag itself. Doing this
            // through the generic edit path instead would restore stock via
            // the "old items" loop below and then immediately re-decrement
            // it via the "new items" loop, since the items themselves never
            // actually changed - netting out to zero and silently failing
            // to actually restore anything.
            if (item.IsReturned && !existing.IsReturned)
            {
                foreach (var lineItem in existing.OrderItems)
                {
                    AdjustStock(lineItem.Product, lineItem.Quantity);
                }
                existing.IsReturned = true;
                existing.UpdatedAt = item.UpdatedAt;
                await dbContext.SaveChangesAsync();
                return new SyncItemResult(item.Id, "Applied");
            }

            if (existing.IsReturned)
            {
                return new SyncItemResult(item.Id, "Rejected", "Cannot edit an invoice that has been marked as returned.");
            }

            // Mirrors OrderService.UpdateAsync's same rule - the client is expected to block this
            // locally before it ever reaches here (see OrdersRepository.update), so this is a
            // backstop against a stale/offline edit that started before the order received a
            // payment or was covered by a receipt.
            if (existing.CoveredByReceipt)
            {
                return new SyncItemResult(item.Id, "Rejected", "Cannot edit an invoice covered by a receipt - return it instead.");
            }

            if (existing.PaidAmount > 0)
            {
                return new SyncItemResult(item.Id, "Rejected", "Cannot edit an invoice that has received a payment - return it instead.");
            }

            var newCustomer = await FindVisibleCustomerAsync(userId, item.CustomerId, scopeToRepId);

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
            // Latitude/Longitude deliberately untouched here - location is
            // "where was I when I made this," captured once at true
            // creation time (see the create branch above), never
            // overwritten by a later edit-sync of the same order.

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

    // Never validated/clamped here - see Product.StockQuantity and the note
    // on OrderService.AdjustStock, which this mirrors exactly. Two devices
    // selling the same product offline both apply their own delta here, and
    // the sum going negative is the correct outcome: it says the shelf is
    // short by that many, which is precisely what a floor would hide.
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
                var customer = await FindVisibleCustomerAsync(userId, item.CustomerId, scopeToRepId);

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
                    UpdatedAt = item.UpdatedAt,
                    Latitude = item.Latitude,
                    Longitude = item.Longitude
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
            // Latitude/Longitude deliberately untouched here - see the
            // identical comment in PushOrderAsync's edit branch.

            await dbContext.SaveChangesAsync();
            return new SyncItemResult(item.Id, "Applied");
        }
        catch (Exception ex)
        {
            return new SyncItemResult(item.Id, "Rejected", ex.Message);
        }
    }

    // See CustomerService.FindOwnedActiveCustomer - same CustomerAccessMode
    // enforcement (Own/All/Restricted), mirrored here so a Rep can't reach a
    // customer it shouldn't through the sync push path instead of the plain
    // REST one. Unlike the REST side this isn't restricted to *active*
    // customers only - PushCustomerAsync/PushOrderAsync/PushReceiptAsync all
    // reuse this same lookup for both fresh inserts and reconciling an
    // update to a customer that might have been archived since.
    private async Task<Customer?> FindVisibleCustomerAsync(Guid userId, Guid customerId, Guid? scopeToRepId)
    {
        var customer = await dbContext.Customers.FirstOrDefaultAsync(c => c.Id == customerId && c.UserId == userId);
        if (customer is null || scopeToRepId is not { } repId) return customer;

        var rep = await dbContext.Reps.FirstOrDefaultAsync(r => r.Id == repId);
        var visible = rep is null || rep.CustomerAccessMode switch
        {
            CustomerAccessMode.All => true,
            CustomerAccessMode.Restricted => customer.CreatedByRepId == repId
                || await dbContext.RepCustomerAccesses.AnyAsync(a => a.RepId == repId && a.CustomerId == customerId),
            _ => customer.CreatedByRepId == repId,
        };
        return visible ? customer : null;
    }

    // Queryable counterpart of FindVisibleCustomerAsync, for PullAsync's
    // whole-list fetch.
    private async Task<IQueryable<Customer>> ApplyCustomerRepScopeAsync(IQueryable<Customer> query, Guid? scopeToRepId)
    {
        if (scopeToRepId is null) return query;

        var rep = await dbContext.Reps.FirstOrDefaultAsync(r => r.Id == scopeToRepId);
        if (rep is null || rep.CustomerAccessMode == CustomerAccessMode.All) return query;

        if (rep.CustomerAccessMode == CustomerAccessMode.Restricted)
        {
            var allowedIds = dbContext.RepCustomerAccesses
                .Where(a => a.RepId == scopeToRepId)
                .Select(a => a.CustomerId);
            return query.Where(c => c.CreatedByRepId == scopeToRepId || allowedIds.Contains(c.Id));
        }

        return query.Where(c => c.CreatedByRepId == scopeToRepId);
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
            if (rep is { ProductAccessMode: ProductAccessMode.RepOwnedOnly })
            {
                query = query.Where(p => p.CreatedByRepId == repId);
            }
            else if (rep is { ProductAccessMode: ProductAccessMode.Selected })
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
