using Fatora.DAL.Entities;

namespace Fatora.DAL.Entites;

public class PasswordResetOtp
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public DateTime ExpiresOnUtc { get; set; }
    public bool Used { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
