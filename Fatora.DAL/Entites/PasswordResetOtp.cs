using Fatora.DAL.Entities;

namespace Fatora.DAL.Entites;

public class PasswordResetOtp
{
    public int Id { get; set; }

    // SHA-256 of the emailed code, never the code itself - this row grants a
    // password reset on the platform Admin, so a leaked database backup or a
    // stray query log must not hand one over. Mirrors how RefreshToken stores
    // its own secret.
    public string CodeHash { get; set; } = string.Empty;

    public DateTime ExpiresOnUtc { get; set; }
    public bool Used { get; set; }

    // Failed verifications against this code. A 6-digit code is only safe
    // because guessing is capped: AdminRecoveryService retires the code once
    // this reaches its limit, so the 9x10^5 keyspace can never be walked. Also
    // why issuing a new code invalidates any outstanding one - without that,
    // N live codes would share the same keyspace and collapse it to ~9x10^5/N.
    public int AttemptCount { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
