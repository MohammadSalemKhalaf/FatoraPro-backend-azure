using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AccountController(IJwtTokenProviderService jwtToken) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> login(LoginRequest request)
    {
        var res = await jwtToken.GenerateToken(request);
        return Ok(res);
    }


}
