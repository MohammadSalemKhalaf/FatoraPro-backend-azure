using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;

namespace Fatora.BL.Services.Classes;

public class CustomerService(AppDbContext dbContext) : ICustomerService
{
    public async Task<CustomerResponse> CreateAsync(Guid userId, CreateCustomerRequest request)
    {
        var customer = new Customer
        {
            Name = request.Name,
            StoreName = request.StoreName,
            PhoneNumber = request.PhoneNumber,
            Street = request.Street,
            City = request.City,
            UserId = userId
        };

        dbContext.Customers.Add(customer);
        await dbContext.SaveChangesAsync();

        return ToResponse(customer);
    }

    public async Task<List<CustomerResponse>> GetAllAsync(Guid userId)
    {
        var customers = await dbContext.Customers
            .Where(c => c.UserId == userId && c.IsActive)
            .ToListAsync();

        return customers.Select(ToResponse).ToList();
    }

    public async Task<CustomerResponse?> GetByIdAsync(Guid userId, int id)
    {
        var customer = await FindOwnedActiveCustomer(userId, id);
        return customer is null ? null : ToResponse(customer);
    }

    public async Task<CustomerResponse?> UpdateAsync(Guid userId, int id, UpdateCustomerRequest request)
    {
        var customer = await FindOwnedActiveCustomer(userId, id);

        if (customer is null)
        {
            return null;
        }

        customer.Name = request.Name;
        customer.StoreName = request.StoreName;
        customer.PhoneNumber = request.PhoneNumber;
        customer.Street = request.Street;
        customer.City = request.City;

        await dbContext.SaveChangesAsync();

        return ToResponse(customer);
    }

    public async Task<bool> DeleteAsync(Guid userId, int id)
    {
        var customer = await FindOwnedActiveCustomer(userId, id);

        if (customer is null)
        {
            return false;
        }

        customer.IsActive = false;
        await dbContext.SaveChangesAsync();

        return true;
    }

    public async Task<List<CustomerResponse>> GetArchivedAsync(Guid userId)
    {
        var customers = await dbContext.Customers
            .Where(c => c.UserId == userId && !c.IsActive)
            .ToListAsync();

        return customers.Select(ToResponse).ToList();
    }

    public async Task<CustomerResponse?> RestoreAsync(Guid userId, int id)
    {
        var customer = await FindOwnedCustomer(userId, id);

        if (customer is null)
        {
            return null;
        }

        customer.IsActive = true;
        await dbContext.SaveChangesAsync();

        return ToResponse(customer);
    }

    public async Task<bool> PermanentDeleteAsync(Guid userId, int id)
    {
        var customer = await FindOwnedCustomer(userId, id);

        if (customer is null)
        {
            return false;
        }

        dbContext.Customers.Remove(customer);
        await dbContext.SaveChangesAsync();

        return true;
    }

    private async Task<Customer?> FindOwnedActiveCustomer(Guid userId, int id) =>
        await dbContext.Customers.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId && c.IsActive);

    private async Task<Customer?> FindOwnedCustomer(Guid userId, int id) =>
        await dbContext.Customers.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

    private static CustomerResponse ToResponse(Customer customer) => new()
    {
        Id = customer.Id,
        Name = customer.Name,
        StoreName = customer.StoreName,
        PhoneNumber = customer.PhoneNumber,
        Street = customer.Street,
        City = customer.City,
        IsActive = customer.IsActive
    };
}
