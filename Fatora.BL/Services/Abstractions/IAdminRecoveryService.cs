namespace Fatora.BL.Services.Abstractions;

public interface IAdminRecoveryService
{
    Task RequestPasswordResetAsync(string userName);
    Task ResetPasswordWithOtpAsync(string userName, string otp, string newPassword);
}
