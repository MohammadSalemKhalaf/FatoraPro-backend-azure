namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;

public sealed record CreateSalesRepRequest(
    [Required, MaxLength(30)] string UserName,
    [Required] string Password,
    [Required] string Name,
    [Required] string PhoneNumber,
    string? BusinessName,
    [Required] string City,
    [Required] string Street
);
