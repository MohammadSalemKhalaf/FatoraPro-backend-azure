using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;

namespace Fatora.BL.Services.Classes;

public class CustomerService(AppDbContext dbContext) : ICustomerService
{
    public async Task<CustomerResponse> CreateAsync(Guid userId, CreateCustomerRequest request, Guid? createdByRepId = null)
    {
        var customer = new Customer
        {
            Name = request.Name,
            StoreName = request.StoreName,
            PhoneNumber = request.PhoneNumber,
            Street = request.Street,
            City = request.City,
            UserId = userId,
            CreatedByRepId = createdByRepId
        };

        dbContext.Customers.Add(customer);
        await dbContext.SaveChangesAsync();

        return ToResponse(customer);
    }

    public async Task<List<CustomerResponse>> GetAllAsync(Guid userId, Guid? scopeToRepId = null)
    {
        var query = dbContext.Customers.Where(c => c.UserId == userId && c.IsActive);
        query = await ApplyRepScopeAsync(query, scopeToRepId);

        var customers = await query.OrderByDescending(c => c.CreatedAt).ToListAsync();

        return customers.Select(ToResponse).ToList();
    }

    public async Task<CustomerResponse> GetByIdAsync(Guid userId, Guid id, Guid? scopeToRepId = null)
    {
        var customer = await FindOwnedActiveCustomer(userId, id, scopeToRepId);

        if (customer is null)
        {
            throw new NotFoundException(nameof(Customer), id);
        }

        return ToResponse(customer);
    }

    public async Task<CustomerResponse> UpdateAsync(Guid userId, Guid id, UpdateCustomerRequest request, Guid? scopeToRepId = null)
    {
        var customer = await FindOwnedActiveCustomer(userId, id, scopeToRepId);

        if (customer is null)
        {
            throw new NotFoundException(nameof(Customer), id);
        }

        customer.Name = request.Name;
        customer.StoreName = request.StoreName;
        customer.PhoneNumber = request.PhoneNumber;
        customer.Street = request.Street;
        customer.City = request.City;

        await dbContext.SaveChangesAsync();

        return ToResponse(customer);
    }

    public async Task DeleteAsync(Guid userId, Guid id, Guid? scopeToRepId = null)
    {
        var customer = await FindOwnedActiveCustomer(userId, id, scopeToRepId);

        if (customer is null)
        {
            throw new NotFoundException(nameof(Customer), id);
        }

        customer.IsActive = false;
        await dbContext.SaveChangesAsync();
    }

    public async Task<List<CustomerResponse>> GetArchivedAsync(Guid userId, Guid? scopeToRepId = null)
    {
        var query = dbContext.Customers.Where(c => c.UserId == userId && !c.IsActive);
        query = await ApplyRepScopeAsync(query, scopeToRepId);

        var customers = await query.ToListAsync();

        return customers.Select(ToResponse).ToList();
    }

    public async Task<CustomerResponse> RestoreAsync(Guid userId, Guid id, Guid? scopeToRepId = null)
    {
        var customer = await FindOwnedCustomer(userId, id, scopeToRepId);

        if (customer is null)
        {
            throw new NotFoundException(nameof(Customer), id);
        }

        customer.IsActive = true;
        await dbContext.SaveChangesAsync();

        return ToResponse(customer);
    }

    public async Task PermanentDeleteAsync(Guid userId, Guid id, Guid? scopeToRepId = null)
    {
        var customer = await FindOwnedCustomer(userId, id, scopeToRepId);

        if (customer is null)
        {
            throw new NotFoundException(nameof(Customer), id);
        }

        dbContext.Customers.Remove(customer);
        await dbContext.SaveChangesAsync();
    }

    // scopeToRepId null means "the owner, sees everything under UserId" -
    // non-null resolves through IsVisibleToRepAsync, which depends on that
    // Rep's own CustomerAccessMode (see Rep.cs): Own (default, and every
    // Rep's actual behavior before this mode existed) means only what it
    // personally created; All opens the owner's entire shared customer
    // book; Restricted limits it to an admin-picked subset, plus whatever
    // it created itself.
    private async Task<Customer?> FindOwnedActiveCustomer(Guid userId, Guid id, Guid? scopeToRepId)
    {
        var customer = await dbContext.Customers.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId && c.IsActive);
        if (customer is null || !await IsVisibleToRepAsync(customer, scopeToRepId)) return null;
        return customer;
    }

    private async Task<Customer?> FindOwnedCustomer(Guid userId, Guid id, Guid? scopeToRepId)
    {
        var customer = await dbContext.Customers.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        if (customer is null || !await IsVisibleToRepAsync(customer, scopeToRepId)) return null;
        return customer;
    }

    // Null scopeToRepId (the owner) never filters. See the CustomerAccessMode
    // rundown above FindOwnedActiveCustomer for what each mode means.
    private async Task<IQueryable<Customer>> ApplyRepScopeAsync(IQueryable<Customer> query, Guid? scopeToRepId)
    {
        if (scopeToRepId is null) return query;

        var rep = await dbContext.Reps.FirstOrDefaultAsync(r => r.Id == scopeToRepId);
        if (rep is null || rep.CustomerAccessMode == CustomerAccessMode.All) return query;

        if (rep.CustomerAccessMode == CustomerAccessMode.Restricted)
        {
            var allowedIds = dbContext.RepCustomerAccesses
                .Where(a => a.RepId == scopeToRepId)
                .Select(a => a.CustomerId);
            return query.Where(c => c.CreatedByRepId == scopeToRepId || allowedIds.Contains(c.Id));
        }

        // Own (default)
        return query.Where(c => c.CreatedByRepId == scopeToRepId);
    }

    private async Task<bool> IsVisibleToRepAsync(Customer customer, Guid? scopeToRepId)
    {
        if (scopeToRepId is null) return true;

        var rep = await dbContext.Reps.FirstOrDefaultAsync(r => r.Id == scopeToRepId);
        if (rep is null || rep.CustomerAccessMode == CustomerAccessMode.All) return true;

        if (rep.CustomerAccessMode == CustomerAccessMode.Restricted)
        {
            return customer.CreatedByRepId == scopeToRepId
                || await dbContext.RepCustomerAccesses.AnyAsync(a => a.RepId == scopeToRepId && a.CustomerId == customer.Id);
        }

        // Own (default)
        return customer.CreatedByRepId == scopeToRepId;
    }

    internal static CustomerResponse ToResponse(Customer customer) => new()
    {
        Id = customer.Id,
        Name = customer.Name,
        StoreName = customer.StoreName,
        PhoneNumber = customer.PhoneNumber,
        Street = customer.Street,
        City = customer.City,
        IsActive = customer.IsActive,
        CreatedAt = customer.CreatedAt,
        UpdatedAt = customer.UpdatedAt
    };
}
