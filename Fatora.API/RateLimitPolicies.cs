namespace Fatora.API;

// Policy names shared between the registrations in Program.cs and the
// [EnableRateLimiting] attributes on the endpoints they protect, so a rename
// can never silently leave an endpoint unlimited.
public static class RateLimitPolicies
{
    public const string SignIn = "sign-in";
    public const string PasswordRecovery = "password-recovery";
}
