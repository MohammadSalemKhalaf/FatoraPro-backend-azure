namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;

public sealed record SyncPushRequest(
    List<CustomerSyncItem> Customers,
    List<ProductSyncItem> Products,
    List<OrderSyncItem> Orders,
    List<Guid> DeletedOrderIds
);

public sealed record CustomerSyncItem(
    [Required] Guid Id,
    [Required] string Name,
    string? StoreName,
    string? PhoneNumber,
    [Required] string Street,
    [Required] string City,
    bool IsActive,
    DateTime UpdatedAt
);

public sealed record ProductSyncItem(
    [Required] Guid Id,
    [Required] string Name,
    string? Description,
    string? ImageUrl,
    decimal PurchasePrice,
    decimal SellPrice,
    bool IsActive,
    DateTime UpdatedAt
);

public sealed record OrderSyncItem(
    [Required] Guid Id,
    [Required] Guid CustomerId,
    DateOnly DueDate,
    [Range(0, 100)] decimal Discount,
    string? Notes,
    [Range(0, double.MaxValue)] decimal PaidAmount,
    DateTime UpdatedAt,
    [Required, MinLength(1)] List<OrderItemSyncItem> Items,
    [Range(0, double.MaxValue)] decimal CashDiscount = 0m
);

public sealed record OrderItemSyncItem(
    [Required] Guid ProductId,
    [Range(1, int.MaxValue)] int Quantity,
    [Range(0.01, double.MaxValue)] decimal UnitPrice
);
