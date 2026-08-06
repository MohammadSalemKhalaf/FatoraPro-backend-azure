namespace Fatora.BL.DTOs.Responses;

/// The current access mode plus, when Restricted, the actual set of
/// allowed ids - fetched on demand only when the owner opens the
/// product/customer access editor, so the plain rep detail view (RepResponse)
/// doesn't have to carry this on every load.
public class RepAccessListResponse
{
    public string Mode { get; set; } = string.Empty;
    public List<Guid> Ids { get; set; } = [];
}
