using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

namespace Fatora.BL.Services.Abstractions;

public interface ISupportContactService
{
    // activeOnly: true for the public, unauthenticated endpoint (every
    // tenant/subscriber sees only channels the Admin currently has
    // switched on); false for the Admin's own management list, which also
    // needs to see - and re-enable - a temporarily disabled one.
    public Task<List<SupportContactResponse>> GetAllAsync(bool activeOnly);
    public Task<SupportContactResponse> CreateAsync(CreateSupportContactRequest request);
    public Task<SupportContactResponse> UpdateAsync(Guid id, UpdateSupportContactRequest request);
    public Task DeleteAsync(Guid id);
    public Task ReorderAsync(ReorderSupportContactsRequest request);
}
