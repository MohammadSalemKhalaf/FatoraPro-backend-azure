using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fatora.BL.Utils;

public static class SeedData
{
    public static async Task seedAuthDataAysnc(IServiceProvider serviceProvider)
    {
        var dbcontext = serviceProvider.GetService<AppDbContext>();
        var passwordHasher = serviceProvider.GetRequiredService<IPasswordHasherService>();


        if (!( await dbcontext.Users.AnyAsync()))
        {
            List<User> users = new()
    {
        new User
        {

            UserName = "admin",
            Password = "Admin123",
            Name = "System Administrator",
            PhoneNumber = "0799000001",
            BusinessName = "Fatora",
            City = "Amman",
            Street = "Queen Rania Street",
            Role = Role.Admin
        },

        new User
        {

            UserName = "ahmad",
            Password = "Password123",
            Name = "Ahmad Ali",
            PhoneNumber = "0799000002",
            BusinessName = "Ahmad Electronics",
            City = "Amman",
            Street = "Gardens Street",
            Role = Role.SalesRep
        },

        new User
        {

            UserName = "mohammad",
            Password = "Password123",
            Name = "Mohammad Hassan",
            PhoneNumber = "0799000003",
            BusinessName = "MH Store",
            City = "Zarqa",
            Street = "King Abdullah Street",
            Role = Role.SalesRep
        },

        new User
        {

            UserName = "sara",
            Password = "Password123",
            Name = "Sara Khaled",
            PhoneNumber = "0799000004",
            BusinessName = "Sara Fashion",
            City = "Irbid",
            Street = "University Street",
            Role = Role.SalesRep
        },

        new User
        {

            UserName = "omar",
            Password = "Password123",
            Name = "Omar Saleh",
            PhoneNumber = "0799000005",
            BusinessName = "Omar Market",
            City = "Aqaba",
            Street = "Beach Road",
            Role = Role.SalesRep
        },

        new User
        {

            UserName = "lina",
            Password = "Password123",
            Name = "Lina Ahmad",
            PhoneNumber = "0799000006",
            BusinessName = "Lina Cosmetics",
            City = "Madaba",
            Street = "Downtown",
            Role = Role.SalesRep
        },

        new User
        {

            UserName = "yousef",
            Password = "Password123",
            Name = "Yousef Mahmoud",
            PhoneNumber = "0799000007",
            BusinessName = "Yousef Mobile",
            City = "Salt",
            Street = "Main Street",
            Role = Role.SalesRep
        },

        new User
        {

            UserName = "reem",
            Password = "Password123",
            Name = "Reem Nasser",
            PhoneNumber = "0799000008",
            BusinessName = "Reem Boutique",
            City = "Jerash",
            Street = "Al Hashmi Street",
            Role = Role.SalesRep
        }
           };// end of users

            foreach (var user in users)
            {
                user.Password = passwordHasher.Hash(user, user.Password);
            }

            await dbcontext.Users.AddRangeAsync(users);
            await dbcontext.SaveChangesAsync();


        }
    }
}
