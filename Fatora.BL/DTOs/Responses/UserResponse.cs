namespace Fatora.BL.DTOs.Responses;

public class UserResponse
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? BusinessName { get; set; }
    public string? LogoUrl { get; set; }
    public string? City { get; set; }
    public string? Street { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? BankName { get; set; }
    public string? AccountNumber { get; set; }
    public string? IBAN { get; set; }
    public string SubscriptionType { get; set; } = string.Empty;
    public DateTime SubscriptionStart { get; set; }
    public DateTime? SubscriptionEnd { get; set; }
    public string AccountStatus { get; set; } = string.Empty;
}
