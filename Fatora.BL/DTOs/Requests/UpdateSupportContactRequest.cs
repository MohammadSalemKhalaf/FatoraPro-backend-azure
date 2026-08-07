using Fatora.DAL.Entites;

namespace Fatora.BL.DTOs.Requests;

public class UpdateSupportContactRequest
{
    public SupportContactType Type { get; set; }
    public string Value { get; set; } = string.Empty;
    public string? Label { get; set; }
    public bool IsActive { get; set; }
}
