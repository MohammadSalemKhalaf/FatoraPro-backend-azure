namespace Fatora.BL.DTOs.Responses;

public class RepResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Only ever populated on creation and regeneration - see RepService -
    // so the raw token isn't handed out again on a plain list/get call.
    public string? QrToken { get; set; }

    public bool IsActive { get; set; }
    public string ProductAccessMode { get; set; } = string.Empty;
    public string CustomerAccessMode { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
