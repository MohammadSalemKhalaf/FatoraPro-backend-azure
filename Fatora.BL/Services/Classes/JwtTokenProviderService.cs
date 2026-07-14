using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Fatora.BL.Services.Classes;

public class JwtTokenProviderService(IConfiguration configuration, AppDbContext dbContext) : IJwtTokenProviderService
{
    public async  Task<JwtTokenResponse> GenerateToken(LoginRequest request)
    {
        var jwtSettings = configuration.GetSection("JwtSettings");

        var issuer = jwtSettings["Issuer"];
        var audience = jwtSettings["Audience"];
        var key = jwtSettings["SecretKey"];
        var expiry = DateTime.UtcNow.AddMinutes(int.Parse(jwtSettings["TokenExpirationInMinutes"]!));

        var user = await dbContext.Users.FirstOrDefaultAsync(u=>u.UserName.Equals(request.UserName));

        if(user is null)
        {
            throw new Exception("User was not found");
        }

        if (!request.Password.Equals(user.Password))
        {
            throw new Exception("User password didn't match");
        }


        //
        var claims = new List<Claim>()
        {
           new Claim(JwtRegisteredClaimNames.Sub,user.Id.ToString()),
           new Claim(ClaimTypes.Role,user.Role.ToString()),
        };

        var descriptor = new SecurityTokenDescriptor()
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiry,
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key!)),
                SecurityAlgorithms.HmacSha256Signature
                )
        };


        var tokenHandler = new JwtSecurityTokenHandler();
        var securityToken = tokenHandler.CreateToken(descriptor);

        return new JwtTokenResponse
        {
            AccessToken = tokenHandler.WriteToken(securityToken),
            RefreshToken="4asdas-asdasd6-asdasd13",
            Expires=expiry
        };

    }
}
