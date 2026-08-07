using Fatora.BL.DTOs.Responses;

namespace Fatora.BL.Services.Abstractions;

public interface ISubAdminAuthService
{
    Task<JwtTokenResponse> LoginByQrAsync(string qrToken);

    // Returns null (not throw) when the given raw token isn't a SubAdmin
    // refresh token at all - lets JwtTokenProviderService.RefreshTokenAsync
    // fall back to this only after its own User/Rep-token lookups miss.
    Task<JwtTokenResponse?> TryRefreshAsync(string refreshToken);
}
