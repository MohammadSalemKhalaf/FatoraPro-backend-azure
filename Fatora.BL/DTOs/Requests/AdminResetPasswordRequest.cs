namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;

public sealed record AdminResetPasswordRequest(
    [Required] string UserName,
    [Required] string Otp,
    [Required] string NewPassword
);
