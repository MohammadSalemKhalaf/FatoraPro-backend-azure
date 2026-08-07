namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;

public sealed record RunAnnualInventoryRequest(
    [Required] string Password,
    bool IncludeCustomers,
    bool IncludeItems,
    bool IncludeInvoices
);
