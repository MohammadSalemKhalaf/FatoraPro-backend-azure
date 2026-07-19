namespace Fatora.BL.DTOs.Responses;

public class UserResponse
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? BusinessName { get; set; }
    public string? LogoUrl { get; set; }
    public string City { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? BankName { get; set; }
    public string? AccountNumber { get; set; }
    public string? IBAN { get; set; }
}
