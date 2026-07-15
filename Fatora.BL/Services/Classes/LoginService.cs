using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
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
            return null;
        }

        if (!passwordHasher.Verify(user, request.Password, user.Password))
        {
            return null;
        }

        return jwtTokenProvider.GenerateToken(user);
    }
}
