namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

public interface IJwtTokenProviderService
{
    public  Task<JwtTokenResponse> GenerateToken(LoginRequest request);
}