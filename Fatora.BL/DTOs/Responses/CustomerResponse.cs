namespace Fatora.BL.DTOs.Responses;

public class CustomerResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? StoreName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Null for anything the owner created - see ProductResponse's identical
    // field for why this needs to reach the client at all (rep-scoped local
    // filtering for the owner's own "filter by rep" view).
    public Guid? CreatedByRepId { get; set; }
}
