namespace Fatora.BL.DTOs.Requests;

public sealed record UpdateBankDetailsRequest(
    string? BankName,
    string? AccountNumber,
    string? IBAN
);
