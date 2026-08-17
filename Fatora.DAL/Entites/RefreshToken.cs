using Fatora.DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fatora.DAL.Entites;

public class RefreshToken
{
    public int Id { get; set; }
    public string Token { get; set; }
    public DateTime ExpiresOnUtc { get; set; }

    // Null while this is the active token for its user. Set the moment a
    // newer one replaces it (see JwtTokenProviderService.IssueRefreshTokenAsync)
    // rather than deleting the row immediately, so a client whose refresh
    // request timed out and retried with this now-superseded token still
    // gets a fresh pair for a short grace window (see
    // JwtTokenProviderService.RefreshRotationGrace) instead of a rejection
    // it has no way to distinguish from "this session is actually dead."
    public DateTime? ReplacedAtUtc { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; }

    // Snapshot of User.SessionVersion at the moment this refresh token was
    // issued - mirrors RepRefreshToken.IssuedAtSessionVersion/
    // SubAdminRefreshToken.IssuedAtSessionVersion exactly. Lets
    // JwtTokenProviderService.RefreshTokenAsync reject a superseded
    // device's refresh attempt even if its access token hasn't naturally
    // expired yet.
    public int IssuedAtSessionVersion { get; set; }
}
