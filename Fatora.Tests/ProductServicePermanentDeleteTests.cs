using Fatora.BL.Exceptions;
using Fatora.BL.Services.Classes;
using Fatora.DAL.Entites;
using Fatora.DAL.Entities;
using Xunit;

namespace Fatora.Tests;

// FK_OrderItems_Products_ProductId is configured onDelete: Cascade (see
// the init migration) - an unconditional hard delete of a product that
// was ever sold would silently take every past invoice's line item for
// it with it. This guard (mirroring CustomerService.PermanentDeleteAsync's
// existing Orders/Receipts check) is what prevents that.
public class ProductServicePermanentDeleteTests
{
    private static (ProductService service, Fatora.DAL.Data.AppDbContext db, User user) Build()
    {
        var db = TestSupport.NewDbContext();
        var user = TestSupport.NewUser();
        db.Users.Add(user);
        db.SaveChanges();
        return (new ProductService(db), db, user);
    }

    private static Product NewProduct(User user) => new()
    {
        Name = "Test Product",
        SellPrice = 10,
        PurchasePrice = 5,
        UserId = user.Id,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task PermanentDeleteAsync_ProductNeverSold_RemovesIt()
    {
        var (service, db, user) = Build();
        var product = NewProduct(user);
        db.Products.Add(product);
        db.SaveChanges();

        await service.PermanentDeleteAsync(user.Id, product.Id);

        Assert.False(db.Products.Any(p => p.Id == product.Id));
    }

    [Fact]
    public async Task PermanentDeleteAsync_ProductSoldOnAnInvoice_ThrowsAndKeepsEverything()
    {
        var (service, db, user) = Build();
        var product = NewProduct(user);
        db.Products.Add(product);

        var customer = new Customer
        {
            Name = "Test Customer",
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Customers.Add(customer);

        var order = new Order
        {
            InvoiceNumber = "INV-TEST-1",
            CustomerId = customer.Id,
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Orders.Add(order);

        var orderItem = new OrderItem
        {
            OrderId = order.Id,
            ProductId = product.Id,
            Quantity = 2,
            UnitPrice = 10,
        };
        db.OrderItems.Add(orderItem);
        db.SaveChanges();

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            service.PermanentDeleteAsync(user.Id, product.Id));

        Assert.Contains("فواتير", exception.Message);

        // Nothing was touched - the product, the order, and its line item
        // must all still be exactly as they were before the rejected call.
        Assert.True(db.Products.Any(p => p.Id == product.Id));
        Assert.True(db.Orders.Any(o => o.Id == order.Id));
        Assert.True(db.OrderItems.Any(oi => oi.Id == orderItem.Id));
    }

    [Fact]
    public async Task PermanentDeleteAsync_ProductWithNoOrderItems_ButOtherProductsSold_StillRemovesIt()
    {
        // Regression guard for an overly-broad guard implementation (e.g.
        // one that checks "does this USER have any order items at all"
        // instead of "does THIS product").
        var (service, db, user) = Build();
        var soldProduct = NewProduct(user);
        var neverSoldProduct = NewProduct(user);
        db.Products.AddRange(soldProduct, neverSoldProduct);

        var customer = new Customer
        {
            Name = "Test Customer",
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Customers.Add(customer);

        var order = new Order
        {
            InvoiceNumber = "INV-TEST-2",
            CustomerId = customer.Id,
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Orders.Add(order);
        db.OrderItems.Add(new OrderItem
        {
            OrderId = order.Id,
            ProductId = soldProduct.Id,
            Quantity = 1,
            UnitPrice = 10,
        });
        db.SaveChanges();

        await service.PermanentDeleteAsync(user.Id, neverSoldProduct.Id);

        Assert.False(db.Products.Any(p => p.Id == neverSoldProduct.Id));
        Assert.True(db.Products.Any(p => p.Id == soldProduct.Id));
    }
}
