namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;

public sealed record RegisterRequest(
    [Required, MaxLength(30)] string UserName,
    [Required] string Password,
    [Required] string Name,
    [Required] string PhoneNumber,
    string? BusinessName = null
);
