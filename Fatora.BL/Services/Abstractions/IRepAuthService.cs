namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Responses;

public interface IRepAuthService
{
    Task<JwtTokenResponse> LoginByQrAsync(string qrToken);

    // Returns null (rather than throwing) when the given raw token isn't a
    // Rep refresh token at all - lets JwtTokenProviderService.
    // RefreshTokenAsync fall back to this only after its own User-token
    // lookup misses, so callers never have to know up front which kind of
    // session they're refreshing.
    Task<JwtTokenResponse?> TryRefreshAsync(string refreshToken);

    Task RevokeSessionAsync(Guid repId);
}
