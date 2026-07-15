namespace Fatora.BL.Services.Abstractions;

using Fatora.DAL.Entities;

public interface IPasswordHasherService
{
    string Hash(User user, string password);
    bool Verify(User user, string password, string hashedPassword);
}
