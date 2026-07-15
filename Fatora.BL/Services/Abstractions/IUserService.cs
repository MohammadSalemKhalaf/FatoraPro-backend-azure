namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

public interface IUserService
{
    public Task<UserResponse?> CreateSalesRepAsync(CreateSalesRepRequest request);
}
