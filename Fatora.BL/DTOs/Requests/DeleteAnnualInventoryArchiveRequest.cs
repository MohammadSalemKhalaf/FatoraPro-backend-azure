namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;

public sealed record DeleteAnnualInventoryArchiveRequest(
    [Required] string Password
);
