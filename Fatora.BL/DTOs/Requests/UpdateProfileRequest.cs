namespace Fatora.BL.DTOs.Requests;

public sealed record UpdateProfileRequest(
    string Name,
    string PhoneNumber,
    string? BusinessName
);
