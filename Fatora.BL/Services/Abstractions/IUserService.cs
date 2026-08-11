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

    // subAdminIdFilter: the Admin's own optional "filter by SubAdmin"
    // narrowing (see SubAdminFilterRow). actingSubAdminId: non-null when a
    // SubAdmin session itself is calling - forces the result to that
    // SubAdmin's own claimed subscribers regardless of subAdminIdFilter, so
    // a SubAdmin can never see the platform's full list or another
    // SubAdmin's claimed set even if it tampers with the query param.
    public Task<List<AdminUserResponse>> GetUsersAsync(string? search, Guid? subAdminIdFilter = null, Guid? actingSubAdminId = null);

    // actingSubAdminId null (top Admin) clears attribution; non-null (a
    // SubAdmin) claims the subscriber for itself - see UserService for why
    // this needs no branching.
    public Task ClaimSubscriberAsync(Guid subscriberId, Guid? actingSubAdminId);
    public Task ResetPasswordAsync(Guid userId, string newPassword, Guid? actingSubAdminId = null);
    public Task<UserResponse> GetProfileAsync(Guid userId);
    public Task<string?> GetLogoUrlAsync(Guid userId);
    public Task<UserResponse> UpdateLogoAsync(Guid userId, string logoUrl);
    public Task<UserResponse> DeleteLogoAsync(Guid userId);
    public Task<UserResponse> UpdateBankDetailsAsync(Guid userId, UpdateBankDetailsRequest request);
    public Task<UserResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request);
    public Task DeleteAccountAsync(Guid userId, string password);
    public Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword);
    // location: the acting Admin/SubAdmin's device position at the moment
    // of the action, if available - see SubAdminAccountAction.Latitude/
    // Longitude. Always optional; never required for the action to
    // succeed.
    public Task SuspendAsync(Guid userId, Guid? actingSubAdminId = null, LocationRequest? location = null);
    public Task ActivateAsync(Guid userId, Guid? actingSubAdminId = null, LocationRequest? location = null);
    public Task<UserResponse> EnableSalesManagerAsync(Guid userId);
}
