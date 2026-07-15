namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Responses;
using Fatora.DAL.Entities;

public interface IJwtTokenProviderService
{
    public JwtTokenResponse GenerateToken(User user);
}