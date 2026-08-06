namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Responses;
using Fatora.DAL.Entites;

public interface IRepAuthService
{
    Task<JwtTokenResponse> LoginByQrAsync(string qrToken);

    // Returns null (rather than throwing) when the given raw token isn't a
    // Rep refresh token at all - lets JwtTokenProviderService.
    // RefreshTokenAsync fall back to this only after its own User-token
    // lookup misses, so callers never have to know up front which kind of
    // session they're refreshing.
    Task<JwtTokenResponse?> TryRefreshAsync(string refreshToken);

    // A narrowly-scoped, short-lived token (Role=RepPendingSync) handed
    // alongside a REP_SESSION_ENDED rejection - see AccountStatusFilter and
    // TryRefreshAsync - so a device that had unsynced offline work when it
    // got cut off still has one last, tightly-bounded channel to upload it
    // for the owner's review instead of losing it outright.
    string GeneratePendingSyncToken(Rep rep);
}
