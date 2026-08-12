using Fatora.API.Extensions;
using Fatora.API.Services;
using Fatora.API.Validators;
using Fatora.API.Validators.UserValidators;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

// A Rep still needs to read the owner's business profile (company name,
// logo, bank details) - it's what its own invoices are branded with - so
// GetProfile alone is opened to "SalesRep,Rep" and resolves via
// GetEffectiveOwnerId(). Every mutating action stays owner-only: a Rep
// never edits the business's own profile.
[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "SalesRep,Rep")]
public class ProfileController(
    IUserService userService,
    IFileStorageService fileStorageService,
    DeleteAccountRequestValidator deleteAccountValidator,
    UpdateProfileRequestValidator updateProfileValidator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetProfile()
    {
        var result = await userService.GetProfileAsync(User.GetEffectiveOwnerId());
        return Ok(result);
    }

    [Authorize(Roles = "SalesRep")]
    [HttpPut]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest request)
    {
        var validationResult = await updateProfileValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await userService.UpdateProfileAsync(User.GetUserId(), request);
        return Ok(result);
    }

    [Authorize(Roles = "SalesRep")]
    [HttpPost("logo")]
    public async Task<IActionResult> UploadLogo(IFormFile file)
    {
        var userId = User.GetUserId();
        var currentLogoUrl = await userService.GetLogoUrlAsync(userId);
        var logoUrl = await fileStorageService.SaveImageAsync(file, userId, "logos", currentLogoUrl);
        var result = await userService.UpdateLogoAsync(userId, logoUrl);
        return Ok(result);
    }

    [Authorize(Roles = "SalesRep")]
    [HttpDelete("logo")]
    public async Task<IActionResult> DeleteLogo()
    {
        var userId = User.GetUserId();
        var currentLogoUrl = await userService.GetLogoUrlAsync(userId);
        if (!string.IsNullOrWhiteSpace(currentLogoUrl))
        {
            await fileStorageService.DeleteImageAsync(currentLogoUrl);
        }
        var result = await userService.DeleteLogoAsync(userId);
        return Ok(result);
    }

    [Authorize(Roles = "SalesRep")]
    [HttpPut("bank-details")]
    public async Task<IActionResult> UpdateBankDetails(UpdateBankDetailsRequest request)
    {
        var result = await userService.UpdateBankDetailsAsync(User.GetUserId(), request);
        return Ok(result);
    }

    [Authorize(Roles = "SalesRep")]
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
