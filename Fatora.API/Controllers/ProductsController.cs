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

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await productService.GetAllAsync(User.GetUserId());
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await productService.GetByIdAsync(User.GetUserId(), id);
        return Ok(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateProductRequest request)
    {
        var validationResult = await updateValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await productService.UpdateAsync(User.GetUserId(), id, request);
        return Ok(result);
    }

    [HttpPost("{id:int}/image")]
    public async Task<IActionResult> UploadImage(int id, IFormFile file)
    {
        var product = await productService.GetByIdAsync(User.GetUserId(), id);
        var imageUrl = await fileStorageService.SaveImageAsync(file, "products", product.ImageUrl);
        var result = await productService.UpdateImageAsync(User.GetUserId(), id, imageUrl);
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
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

    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id)
    {
        var result = await productService.RestoreAsync(User.GetUserId(), id);
        return Ok(result);
    }

    [HttpDelete("{id:int}/permanent")]
    public async Task<IActionResult> PermanentDelete(int id)
    {
        await productService.PermanentDeleteAsync(User.GetUserId(), id);
        return NoContent();
    }
}
