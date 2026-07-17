using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fatora.BL.Services.Classes;

public class UserService(AppDbContext dbContext, IPasswordHasherService passwordHasher) : IUserService
{
    public async Task<UserResponse> CreateSalesRepAsync(CreateSalesRepRequest request)
    {
        var usernameTaken = await dbContext.Users.AnyAsync(u => u.UserName == request.UserName);

        if (usernameTaken)
        {
            throw new ConflictException(nameof(User), request.UserName);
        }

        var user = new User
        {
            UserName = request.UserName,
            Password = request.Password,
            Name = request.Name,
            PhoneNumber = request.PhoneNumber,
            BusinessName = request.BusinessName,
            City = request.City,
            Street = request.Street,
            Role = Role.SalesRep
        };

        user.Password = passwordHasher.Hash(user, user.Password);

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        return ToResponse(user);
    }

    public async Task<string?> GetLogoUrlAsync(Guid userId)
    {
        var user = await FindUser(userId);
        return user.LogoUrl;
    }

    public async Task<UserResponse> UpdateLogoAsync(Guid userId, string logoUrl)
    {
        var user = await FindUser(userId);

        user.LogoUrl = logoUrl;
        await dbContext.SaveChangesAsync();

        return ToResponse(user);
    }

    private async Task<User> FindUser(Guid userId)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null)
        {
            throw new NotFoundException(nameof(User), userId);
        }

        return user;
    }

    private static UserResponse ToResponse(User user) => new()
    {
        Id = user.Id,
        UserName = user.UserName,
        Name = user.Name,
        PhoneNumber = user.PhoneNumber,
        BusinessName = user.BusinessName,
        LogoUrl = user.LogoUrl,
        City = user.City,
        Street = user.Street,
        Role = user.Role.ToString()
    };
}
