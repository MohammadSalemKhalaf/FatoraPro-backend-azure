using Fatora.API.Extensions;
using Fatora.API.Services;
using Fatora.API.Validators;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "SalesRep")]
public class ProfileController(
    IUserService userService,
    IFileStorageService fileStorageService,
    DeleteAccountRequestValidator deleteAccountValidator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetProfile()
    {
        var result = await userService.GetProfileAsync(User.GetUserId());
        return Ok(result);
    }

    [HttpPost("logo")]
    public async Task<IActionResult> UploadLogo(IFormFile file)
    {
        var userId = User.GetUserId();
        var currentLogoUrl = await userService.GetLogoUrlAsync(userId);
        var logoUrl = await fileStorageService.SaveImageAsync(file, "logos", currentLogoUrl);
        var result = await userService.UpdateLogoAsync(userId, logoUrl);
        return Ok(result);
    }

    [HttpPut("bank-details")]
    public async Task<IActionResult> UpdateBankDetails(UpdateBankDetailsRequest request)
    {
        var result = await userService.UpdateBankDetailsAsync(User.GetUserId(), request);
        return Ok(result);
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAccount(DeleteAccountRequest request)
    {
        var validationResult = await deleteAccountValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        await userService.DeleteAccountAsync(User.GetUserId(), request.Password);
        return NoContent();
    }
}
