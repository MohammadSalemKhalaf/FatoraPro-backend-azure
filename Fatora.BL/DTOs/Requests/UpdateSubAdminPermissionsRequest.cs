namespace Fatora.BL.DTOs.Requests;

public class UpdateSubAdminPermissionsRequest
{
    public bool CanActivateMonthly { get; set; }
    public bool CanActivateAnnual { get; set; }
    public bool CanActivateCustomMonths { get; set; }
    public bool CanManageAccount { get; set; }
    public bool CanResetPassword { get; set; }
}
