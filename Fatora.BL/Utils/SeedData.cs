using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace Fatora.BL.Utils;

public static class SeedData
{
    public static async Task seedAuthDataAysnc(IServiceProvider serviceProvider)
    {
        var dbcontext = serviceProvider.GetRequiredService<AppDbContext>();
        var passwordHasher = serviceProvider.GetRequiredService<IPasswordHasherService>();
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();

        var adminSeed = configuration.GetSection("AdminSeed");
        var userName = adminSeed["UserName"]!;

        // Deliberately not in appsettings.json - set via the AdminSeed__Password environment
        // variable so the real admin password is never committed to source control.
        var password = adminSeed["Password"];
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException(
                "AdminSeed:Password is not configured. Set the AdminSeed__Password environment variable before starting the app.");
        }

        if (await dbcontext.Users.AnyAsync(u => u.UserName == userName))
        {
            return;
        }

        var start = DateTime.UtcNow;
        var admin = new User
        {
            UserName = userName,
            Password = password,
            Name = "System Administrator",
            PhoneNumber = "0000000000",
            Role = Role.Admin,
            SubscriptionType = SubscriptionType.Lifetime,
            SubscriptionStart = start,
            CreatedAt = start
        };

        admin.Password = passwordHasher.Hash(admin, admin.Password);

        await dbcontext.Users.AddAsync(admin);
        await dbcontext.SaveChangesAsync();
    }
}
