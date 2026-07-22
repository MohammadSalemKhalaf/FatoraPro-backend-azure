namespace Fatora.BL.DTOs.Responses;

public class AdminUserResponse
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string SubscriptionType { get; set; } = string.Empty;
    public DateTime SubscriptionStart { get; set; }
    public DateTime? SubscriptionEnd { get; set; }
    public string AccountStatus { get; set; } = string.Empty;
}
