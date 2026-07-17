using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Microsoft.EntityFrameworkCore;

namespace Fatora.BL.Services.Classes;

public class LoginService(AppDbContext dbContext, IPasswordHasherService passwordHasher, IJwtTokenProviderService jwtTokenProvider) : ILoginService
{
    public async Task<JwtTokenResponse?> Login(LoginRequest request)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.UserName.Equals(request.UserName));

        if (user is null)
        {
            throw new NotFoundException($"User with this {request.UserName} was not found");;
        }

        if (!passwordHasher.Verify(user, request.Password, user.Password))
        {
            throw new NotFoundException($"Wrong password for User ");;
        }

        return await jwtTokenProvider.GenerateToken(user);
    }
}
