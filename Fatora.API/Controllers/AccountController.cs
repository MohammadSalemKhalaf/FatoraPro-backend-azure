using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AccountController(ILoginService loginService, IJwtTokenProviderService jwtTokenProvider) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> login(LoginRequest request)
    {
        var res = await loginService.Login(request);

        if (res is null)
        {
            return Unauthorized(new { message = "Invalid username or password" });
        }

        return Ok(res);
    }

    [HttpPost("refresh-token")]
    public async Task<IActionResult> refreshToken(RefreshTokenRequest request)
    {
        var res = await jwtTokenProvider.RefreshTokenAsync(request.RefreshToken);

        if (res is null)
        {
            return Unauthorized(new { message = "Invalid or expired refresh token" });
        }

        return Ok(res);
    }
}
