namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;

public sealed record OrderItemRequest(
    [Required] int ProductId,
    [Range(1, int.MaxValue)] int Quantity
);
