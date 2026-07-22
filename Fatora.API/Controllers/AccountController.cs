using Fatora.API.Extensions;
using Fatora.API.Validators;
using Fatora.API.Validators.UserValidators;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AccountController(
    ILoginService loginService,
    IJwtTokenProviderService jwtTokenProvider,
    IUserService userService,
    LoginRequestValidator loginRequestValidator,
    RefreshTokenValidator refreshTokenValidator,
    ChangePasswordRequestValidator changePasswordValidator,
    RegisterRequestValidator registerRequestValidator) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var validationResult = await registerRequestValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await userService.RegisterAsync(request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> login(LoginRequest request)
    {
        var validationResult = await loginRequestValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var res = await loginService.Login(request);
        return Ok(res);
    }

    [HttpPost("refresh-token")]
    public async Task<IActionResult> refreshToken(RefreshTokenRequest request)
    {
        var validationResult = await refreshTokenValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var res = await jwtTokenProvider.RefreshTokenAsync(request.RefreshToken);
        return Ok(res);
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await jwtTokenProvider.LogoutAsync(User.GetUserId());
        return NoContent();
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var validationResult = await changePasswordValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        await userService.ChangePasswordAsync(User.GetUserId(), request.CurrentPassword, request.NewPassword);
        return NoContent();
    }
}
