namespace Fatora.DAL.Entites;

/// Mirrors RefreshToken exactly, but for a Rep session rather than a User
/// one - kept as a parallel table instead of widening RefreshToken itself,
/// since RefreshToken's issue/rotate/revoke logic in JwtTokenProviderService
/// is hardcoded to User at several call sites; duplicating this small table
/// is cheaper than branching all of them by principal type.
public class RepRefreshToken
{
    public int Id { get; set; }
    public string Token { get; set; }
    public DateTime ExpiresOnUtc { get; set; }

    // See RefreshToken.ReplacedAtUtc - same grace-window reasoning, applied
    // to a Rep's own refresh token.
    public DateTime? ReplacedAtUtc { get; set; }

    public Guid RepId { get; set; }
    public Rep Rep { get; set; }

    // Snapshot of Rep.SessionVersion at the moment this token was issued -
    // lets RepAuthService.TryRefreshAsync tell "this token belongs to a
    // session the owner has since ended (logout or deactivate)" apart from
    // "this token is still current", without needing to delete the row the
    // instant a session ends. See RepService.LogoutAsync for why the row is
    // deliberately no longer deleted on logout.
    public int IssuedAtSessionVersion { get; set; }
}
