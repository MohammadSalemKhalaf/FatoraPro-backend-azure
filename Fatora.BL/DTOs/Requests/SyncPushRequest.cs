namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;

public sealed record SyncPushRequest(
    List<CustomerSyncItem> Customers,
    List<ProductSyncItem> Products,
    List<OrderSyncItem> Orders,
    List<ReceiptSyncItem> Receipts,
    List<Guid> DeletedOrderIds
);

public sealed record CustomerSyncItem(
    [Required] Guid Id,
    [Required] string Name,
    string? StoreName,
    string? PhoneNumber,
    string? Street,
    string? City,
    bool IsActive,
    DateTime UpdatedAt
);

public sealed record ProductSyncItem(
    [Required] Guid Id,
    [Required] string Name,
    string? Description,
    string? ImageUrl,
    string? Barcode,
    decimal PurchasePrice,
    decimal SellPrice,
    bool IsActive,
    DateTime UpdatedAt,
    int? StockQuantity = null
);

public sealed record OrderSyncItem(
    [Required] Guid Id,
    [Required] Guid CustomerId,
    DateOnly? DueDate,
    [Range(0, 100)] decimal Discount,
    string? Notes,
    [Range(0, double.MaxValue)] decimal PaidAmount,
    DateTime UpdatedAt,
    [Required, MinLength(1)] List<OrderItemSyncItem> Items,
    [Range(0, double.MaxValue)] decimal CashDiscount = 0m,
    bool CoveredByReceipt = false,
    bool IsReturned = false
);

public sealed record OrderItemSyncItem(
    [Required] Guid ProductId,
    [Range(1, int.MaxValue)] int Quantity,
    [Range(0.01, double.MaxValue)] decimal UnitPrice
);

public sealed record ReceiptSyncItem(
    [Required] Guid Id,
    [Required] Guid CustomerId,
    [Range(0.01, double.MaxValue)] decimal Amount,
    bool IsActive,
    DateTime UpdatedAt
);
