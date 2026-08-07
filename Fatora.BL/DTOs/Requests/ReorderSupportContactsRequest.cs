namespace Fatora.BL.DTOs.Requests;

// The full, ordered list of contact ids - DisplayOrder is set to each id's
// index in this list. Any id not owned by an existing SupportContact is
// silently ignored (same defensive-drop reasoning as
// RepService.SetProductAccessAsync).
public class ReorderSupportContactsRequest
{
    public List<Guid> OrderedIds { get; set; } = [];
}
