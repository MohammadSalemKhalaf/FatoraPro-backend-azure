namespace Fatora.DAL.Entites;

/// Mirrors RepRefreshToken exactly, but for a SubAdmin session - kept as its
/// own parallel table for the same reason Rep needed one instead of widening
/// RefreshToken: JwtTokenProviderService's issue/rotate/revoke logic is
/// hardcoded to User at several call sites.
public class SubAdminRefreshToken
{
    public int Id { get; set; }
    public required string Token { get; set; }
    public DateTime ExpiresOnUtc { get; set; }

    // See RefreshToken.ReplacedAtUtc - same grace-window reasoning, applied
    // to a SubAdmin's own refresh token.
    public DateTime? ReplacedAtUtc { get; set; }

    public Guid SubAdminId { get; set; }
    public SubAdmin SubAdmin { get; set; } = null!;

    // Snapshot of SubAdmin.SessionVersion at issue time - same reasoning as
    // RepRefreshToken.IssuedAtSessionVersion.
    public int IssuedAtSessionVersion { get; set; }
}
