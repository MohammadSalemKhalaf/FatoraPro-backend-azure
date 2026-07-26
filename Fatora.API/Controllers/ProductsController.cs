using Fatora.API.Extensions;
using Fatora.API.Services;
using Fatora.API.Validators.ProductValidators;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "SalesRep")]
public class ProductsController(
    IProductService productService,
    IFileStorageService fileStorageService,
    CreateProductRequestValidator createValidator,
    UpdateProductRequestValidator updateValidator) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateProductRequest request)
    {
        var validationResult = await createValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await productService.CreateAsync(User.GetUserId(), request);
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
            var all = await productService.GetAllAsync(User.GetUserId());
            return Ok(all);
        }

        var result = await productService.GetPagedAsync(User.GetUserId(), skip ?? 0, Math.Clamp(take ?? 20, 1, 100));
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await productService.GetByIdAsync(User.GetUserId(), id);
        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateProductRequest request)
    {
        var validationResult = await updateValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await productService.UpdateAsync(User.GetUserId(), id, request);
        return Ok(result);
    }

    [HttpPost("{id:guid}/image")]
    public async Task<IActionResult> UploadImage(Guid id, IFormFile file)
    {
        var product = await productService.GetByIdAsync(User.GetUserId(), id);
        var imageUrl = await fileStorageService.SaveImageAsync(file, "products", product.ImageUrl);
        var result = await productService.UpdateImageAsync(User.GetUserId(), id, imageUrl);
        return Ok(result);
    }

    [HttpDelete("{id:guid}/image")]
    public async Task<IActionResult> DeleteImage(Guid id)
    {
        var userId = User.GetUserId();
        var product = await productService.GetByIdAsync(userId, id);
        if (!string.IsNullOrWhiteSpace(product.ImageUrl))
        {
            await fileStorageService.DeleteImageAsync(product.ImageUrl);
        }
        var result = await productService.DeleteImageAsync(userId, id);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await productService.DeleteAsync(User.GetUserId(), id);
        return NoContent();
    }

    [HttpGet("archived")]
    public async Task<IActionResult> GetArchived()
    {
        var result = await productService.GetArchivedAsync(User.GetUserId());
        return Ok(result);
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id)
    {
        var result = await productService.RestoreAsync(User.GetUserId(), id);
        return Ok(result);
    }

    [HttpDelete("{id:guid}/permanent")]
    public async Task<IActionResult> PermanentDelete(Guid id)
    {
        await productService.PermanentDeleteAsync(User.GetUserId(), id);
        return NoContent();
    }
}
