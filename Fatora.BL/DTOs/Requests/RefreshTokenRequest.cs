namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;

public sealed record RefreshTokenRequest([Required] string RefreshToken);
