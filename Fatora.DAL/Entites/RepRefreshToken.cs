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
    public Guid RepId { get; set; }
    public Rep Rep { get; set; }
}
