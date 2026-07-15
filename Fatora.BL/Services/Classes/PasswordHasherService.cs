using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Entities;
using Microsoft.AspNetCore.Identity;

namespace Fatora.BL.Services.Classes;

public class PasswordHasherService : IPasswordHasherService
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    public bool Verify(User user, string password, string hashedPassword) =>
        _hasher.VerifyHashedPassword(user, hashedPassword, password) != PasswordVerificationResult.Failed;
}
