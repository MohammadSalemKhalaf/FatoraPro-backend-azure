namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;

public sealed record UpdateCustomerRequest(
    [Required] string Name,
    string? StoreName,
    string? PhoneNumber,
    [Required] string Street,
    [Required] string City
);
