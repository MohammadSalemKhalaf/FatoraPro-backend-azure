namespace Fatora.BL.DTOs.Responses;


public class JwtTokenResponse
{
    public string AccessToken{get;set;}
    public string RefreshToken { get; set; }
    public DateTime Expires { get; set; }
}