namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;

public sealed record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required] string NewPassword
);
