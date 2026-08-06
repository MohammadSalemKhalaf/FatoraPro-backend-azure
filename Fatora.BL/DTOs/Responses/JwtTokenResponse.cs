namespace Fatora.BL.DTOs.Responses;


public class JwtTokenResponse
{
    public string AccessToken{get;set;}
    public string RefreshToken { get; set; }
    public DateTime Expires { get; set; }

    // Only ever populated for a Rep session (see RepAuthService) - a Rep
    // has no username to show in its place, so this is what the client
    // displays instead. Always null for a normal /account/login response.
    public string? Name { get; set; }
}