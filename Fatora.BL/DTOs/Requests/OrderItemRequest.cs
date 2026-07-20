namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;

public sealed record OrderItemRequest(
    [Required] Guid ProductId,
    [Range(1, int.MaxValue)] int Quantity
);
