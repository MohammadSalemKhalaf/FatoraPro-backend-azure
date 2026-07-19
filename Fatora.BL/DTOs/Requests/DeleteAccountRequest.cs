namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;

public sealed record DeleteAccountRequest([Required] string Password);
