namespace Fatora.BL.DTOs.Responses;

public class SubAdminResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Only ever populated on creation and regeneration - see SubAdminService -
    // so the raw token isn't handed out again on a plain list/get call.
    public string? QrToken { get; set; }

    public bool IsActive { get; set; }
    public bool IsHidden { get; set; }
    public bool CanActivateMonthly { get; set; }
    public bool CanActivateAnnual { get; set; }
    public bool CanActivateCustomMonths { get; set; }
    public bool CanManageAccount { get; set; }
    public DateTime CreatedAt { get; set; }
}
