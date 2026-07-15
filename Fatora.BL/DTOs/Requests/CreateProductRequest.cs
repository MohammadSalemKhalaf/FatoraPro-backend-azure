namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;

public sealed record CreateProductRequest(
    [Required] string Name,
    [Range(0, double.MaxValue)] decimal PurchasePrice,
    [Range(0, double.MaxValue)] decimal SellPrice
);
