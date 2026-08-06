using Fatora.API.Extensions;
using Fatora.API.Services;
using Fatora.API.Validators.ProductValidators;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

// Class-level "SalesRep,Rep" covers the read actions (a Rep only ever
// picks from the business's existing catalog) - every mutating action
// below overrides back down to "SalesRep" only, since Reps never create,
// edit, or delete products.
[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "SalesRep,Rep")]
public class ProductsController(
    IProductService productService,
    IFileStorageService fileStorageService,
    CreateProductRequestValidator createValidator,
    UpdateProductRequestValidator updateValidator) : ControllerBase
{
    [Authorize(Roles = "SalesRep")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateProductRequest request)
    {
        var validationResult = await createValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await productService.CreateAsync(User.GetEffectiveOwnerId(), request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    // skip/take are optional and additive - omitting both preserves the
    // original "return everything" behavior existing callers (invoice
    // product picker, data export, barcode lookups) still rely on.
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? skip, [FromQuery] int? take)
    {
        if (skip is null && take is null)
        {
            var all = await productService.GetAllAsync(User.GetEffectiveOwnerId(), User.GetRepIdOrNull());
            return Ok(all);
        }

        var result = await productService.GetPagedAsync(
            User.GetEffectiveOwnerId(), skip ?? 0, Math.Clamp(take ?? 20, 1, 100), User.GetRepIdOrNull());
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await productService.GetByIdAsync(User.GetEffectiveOwnerId(), id, User.GetRepIdOrNull());
        return Ok(result);
    }

    [Authorize(Roles = "SalesRep")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateProductRequest request)
    {
        var validationResult = await updateValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await productService.UpdateAsync(User.GetEffectiveOwnerId(), id, request);
        return Ok(result);
    }

    [Authorize(Roles = "SalesRep")]
    [HttpPost("{id:guid}/image")]
    public async Task<IActionResult> UploadImage(Guid id, IFormFile file)
    {
        var product = await productService.GetByIdAsync(User.GetEffectiveOwnerId(), id);
        var imageUrl = await fileStorageService.SaveImageAsync(file, "products", product.ImageUrl);
        var result = await productService.UpdateImageAsync(User.GetEffectiveOwnerId(), id, imageUrl);
        return Ok(result);
    }

    [Authorize(Roles = "SalesRep")]
    [HttpDelete("{id:guid}/image")]
    public async Task<IActionResult> DeleteImage(Guid id)
    {
        var userId = User.GetEffectiveOwnerId();
        var product = await productService.GetByIdAsync(userId, id);
        if (!string.IsNullOrWhiteSpace(product.ImageUrl))
        {
            await fileStorageService.DeleteImageAsync(product.ImageUrl);
        }
        var result = await productService.DeleteImageAsync(userId, id);
        return Ok(result);
    }

    [Authorize(Roles = "SalesRep")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await productService.DeleteAsync(User.GetEffectiveOwnerId(), id);
        return NoContent();
    }

    [Authorize(Roles = "SalesRep")]
    [HttpGet("archived")]
    public async Task<IActionResult> GetArchived()
    {
        var result = await productService.GetArchivedAsync(User.GetEffectiveOwnerId());
        return Ok(result);
    }

    [Authorize(Roles = "SalesRep")]
    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id)
    {
        var result = await productService.RestoreAsync(User.GetEffectiveOwnerId(), id);
        return Ok(result);
    }

    [Authorize(Roles = "SalesRep")]
    [HttpDelete("{id:guid}/permanent")]
    public async Task<IActionResult> PermanentDelete(Guid id)
    {
        await productService.PermanentDeleteAsync(User.GetEffectiveOwnerId(), id);
        return NoContent();
    }
}
