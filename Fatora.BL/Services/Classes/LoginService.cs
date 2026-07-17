using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Microsoft.EntityFrameworkCore;

namespace Fatora.BL.Services.Classes;

public class LoginService(AppDbContext dbContext, IPasswordHasherService passwordHasher, IJwtTokenProviderService jwtTokenProvider) : ILoginService
{
    public async Task<JwtTokenResponse> Login(LoginRequest request)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.UserName.Equals(request.UserName));

        // Deliberately the same exception/message for "user not found" and "wrong password" -
        // distinguishing them would let a caller enumerate valid usernames.
        if (user is null || !passwordHasher.Verify(user, request.Password, user.Password))
        {
            throw new UnauthorizedException("Invalid username or password");
        }

        return await jwtTokenProvider.GenerateToken(user);
    }
}
