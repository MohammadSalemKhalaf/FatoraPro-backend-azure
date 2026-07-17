using Fatora.API.Extensions;
using Fatora.API.Services;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "SalesRep")]
public class ProfileController(IUserService userService, IFileStorageService fileStorageService) : ControllerBase
{
    [HttpPost("logo")]
    public async Task<IActionResult> UploadLogo(IFormFile file)
    {
        var userId = User.GetUserId();
        var currentLogoUrl = await userService.GetLogoUrlAsync(userId);
        var logoUrl = await fileStorageService.SaveImageAsync(file, "logos", currentLogoUrl);
        var result = await userService.UpdateLogoAsync(userId, logoUrl);
        return Ok(result);
    }
}
