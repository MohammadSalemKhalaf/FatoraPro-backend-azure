using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fatora.BL.Services.Classes;

public class UserService(AppDbContext dbContext, IPasswordHasherService passwordHasher) : IUserService
{
    public async Task<UserResponse?> CreateSalesRepAsync(CreateSalesRepRequest request)
    {
        var usernameTaken = await dbContext.Users.AnyAsync(u => u.UserName == request.UserName);

        if (usernameTaken)
        {
            return null;
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

        return new UserResponse
        {
            Id = user.Id,
            UserName = user.UserName,
            Name = user.Name,
            PhoneNumber = user.PhoneNumber,
            BusinessName = user.BusinessName,
            City = user.City,
            Street = user.Street,
            Role = user.Role.ToString()
        };
    }
}
