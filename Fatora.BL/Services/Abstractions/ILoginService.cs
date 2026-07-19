namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

public interface ILoginService
{
    public Task<JwtTokenResponse> Login(LoginRequest request);
}
