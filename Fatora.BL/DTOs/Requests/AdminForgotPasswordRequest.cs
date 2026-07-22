namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;

public sealed record AdminForgotPasswordRequest(
    [Required] string UserName
);
