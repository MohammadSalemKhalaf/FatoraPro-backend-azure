namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

public interface IUserService
{
    public Task<UserResponse> CreateSalesRepAsync(CreateSalesRepRequest request);
    public Task<UserResponse> RegisterAsync(RegisterRequest request);
    // actingSubAdminId: null means the caller is the top Admin, who always
    // bypasses the permission gate below. Non-null means a SubAdmin is
    // calling (see UsersController) - its own granted CanActivate* flags are
    // checked against the requested SubscriptionType before anything is
    // written.
    public Task<AdminUserResponse> UpdateSubscriptionAsync(Guid userId, UpdateSubscriptionRequest request, Guid? actingSubAdminId = null);
    public Task<List<AdminUserResponse>> GetUsersAsync(string? search);
    public Task ResetPasswordAsync(Guid userId, string newPassword);
    public Task<UserResponse> GetProfileAsync(Guid userId);
    public Task<string?> GetLogoUrlAsync(Guid userId);
    public Task<UserResponse> UpdateLogoAsync(Guid userId, string logoUrl);
    public Task<UserResponse> DeleteLogoAsync(Guid userId);
    public Task<UserResponse> UpdateBankDetailsAsync(Guid userId, UpdateBankDetailsRequest request);
    public Task<UserResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request);
    public Task DeleteAccountAsync(Guid userId, string password);
    public Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword);
    public Task SuspendAsync(Guid userId);
    public Task ActivateAsync(Guid userId);
    public Task<UserResponse> EnableSalesManagerAsync(Guid userId);
}
