namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;

public sealed record UpdateOrderRequest(
    [Required] int CustomerId,
    DateOnly DueDate,
    [Range(0, 100)] decimal Discount,
    string? Notes,
    [Required, MinLength(1)] List<OrderItemRequest> Items
);
