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
        var customers = await dbContext.Customers
            .Where(c => c.UserId == userId && c.IsActive
                && (scopeToRepId == null || c.CreatedByRepId == scopeToRepId))
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

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
        var customers = await dbContext.Customers
            .Where(c => c.UserId == userId && !c.IsActive
                && (scopeToRepId == null || c.CreatedByRepId == scopeToRepId))
            .ToListAsync();

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
    // non-null (always a Rep's own id in practice, see
    // ClaimsPrincipalExtensions.GetRepIdOrNull) means "only what this Rep
    // itself created," never another Rep's or the owner's own directly-
    // created customers - each sub-account's customer book starts empty and
    // stays entirely separate, per the feature's own design.
    private async Task<Customer?> FindOwnedActiveCustomer(Guid userId, Guid id, Guid? scopeToRepId) =>
        await dbContext.Customers.FirstOrDefaultAsync(c =>
            c.Id == id && c.UserId == userId && c.IsActive
            && (scopeToRepId == null || c.CreatedByRepId == scopeToRepId));

    private async Task<Customer?> FindOwnedCustomer(Guid userId, Guid id, Guid? scopeToRepId) =>
        await dbContext.Customers.FirstOrDefaultAsync(c =>
            c.Id == id && c.UserId == userId
            && (scopeToRepId == null || c.CreatedByRepId == scopeToRepId));

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
