using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;

namespace Fatora.BL.Services.Classes;

// Platform-wide config (not per-tenant), so no owner-scoping anywhere in
// this class - every method operates on the single shared SupportContacts
// table.
public class SupportContactService(AppDbContext dbContext) : ISupportContactService
{
    public async Task<List<SupportContactResponse>> GetAllAsync(bool activeOnly)
    {
        var query = dbContext.SupportContacts.AsQueryable();

        if (activeOnly)
        {
            query = query.Where(c => c.IsActive);
        }

        var contacts = await query.OrderBy(c => c.DisplayOrder).ToListAsync();

        return contacts.Select(ToResponse).ToList();
    }

    public async Task<SupportContactResponse> CreateAsync(CreateSupportContactRequest request)
    {
        // New rows sort last by default - DisplayOrder is otherwise only
        // ever touched by ReorderAsync.
        var maxOrder = await dbContext.SupportContacts.Select(c => (int?)c.DisplayOrder).MaxAsync() ?? -1;

        var contact = new SupportContact
        {
            Type = request.Type,
            Value = request.Value,
            Label = request.Label,
            DisplayOrder = maxOrder + 1,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.SupportContacts.Add(contact);
        await dbContext.SaveChangesAsync();

        return ToResponse(contact);
    }

    public async Task<SupportContactResponse> UpdateAsync(Guid id, UpdateSupportContactRequest request)
    {
        var contact = await FindContact(id);

        contact.Type = request.Type;
        contact.Value = request.Value;
        contact.Label = request.Label;
        contact.IsActive = request.IsActive;

        await dbContext.SaveChangesAsync();

        return ToResponse(contact);
    }

    public async Task DeleteAsync(Guid id)
    {
        var contact = await FindContact(id);

        // Hard delete is fine here - this is admin-configured platform
        // settings, not user data with a history worth preserving (unlike
        // Rep.IsHidden/Order.IsDeleted).
        dbContext.SupportContacts.Remove(contact);
        await dbContext.SaveChangesAsync();
    }

    public async Task ReorderAsync(ReorderSupportContactsRequest request)
    {
        var contacts = await dbContext.SupportContacts
            .Where(c => request.OrderedIds.Contains(c.Id))
            .ToListAsync();

        var contactsById = contacts.ToDictionary(c => c.Id);

        for (var i = 0; i < request.OrderedIds.Count; i++)
        {
            if (contactsById.TryGetValue(request.OrderedIds[i], out var contact))
            {
                contact.DisplayOrder = i;
            }
        }

        await dbContext.SaveChangesAsync();
    }

    private async Task<SupportContact> FindContact(Guid id)
    {
        var contact = await dbContext.SupportContacts.FirstOrDefaultAsync(c => c.Id == id);

        if (contact is null)
        {
            throw new NotFoundException(nameof(SupportContact), id);
        }

        return contact;
    }

    private static SupportContactResponse ToResponse(SupportContact contact) => new()
    {
        Id = contact.Id,
        Type = contact.Type.ToString(),
        Value = contact.Value,
        Label = contact.Label,
        DisplayOrder = contact.DisplayOrder,
        IsActive = contact.IsActive,
        CreatedAt = contact.CreatedAt
    };
}
