namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Responses;
using Fatora.DAL.Entities;

public interface IJwtTokenProviderService
{
    public Task<JwtTokenResponse> GenerateToken(User user);
    public Task<JwtTokenResponse> RefreshTokenAsync(string refreshToken);
    public Task LogoutAsync(Guid userId);
}