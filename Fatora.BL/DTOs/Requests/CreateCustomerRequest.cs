namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;

public sealed record CreateCustomerRequest(
    [Required] string Name,
    string? StoreName,
    string? PhoneNumber,
    string? Street,
    string? City
);
