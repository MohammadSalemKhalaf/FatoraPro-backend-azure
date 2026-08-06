using Fatora.API.Extensions;
using Fatora.API.Services;
using Fatora.API.Validators.ProductValidators;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

// Class-level "SalesRep,Rep" covers every read action (a Rep only ever
// picks from the business's existing catalog for read purposes) plus
// Create/Update/Delete below, which are additionally scoped to "a Rep can
// only touch a product it created itself" inside ProductService - see
// ProductService.CreateAsync/IsOwnRepCreation. UploadImage/DeleteImage stay
// owner-only for now (not part of the requested rep-owned-products scope).
[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "SalesRep,Rep")]
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

        var result = await productService.CreateAsync(User.GetEffectiveOwnerId(), request, User.GetRepIdOrNull());
        return StatusCode(StatusCodes.Status201Created, result);
    }

    // skip/take are optional and additive - omitting both preserves the
    // original "return everything" behavior existing callers (invoice
    // product picker, data export, barcode lookups) still rely on. repId
    // lets the owner see what one particular rep can actually see - an All
    // rep returns everything, a Restricted one only its granted subset (see
    // ProductService.ApplyRepScopeAsync) - a Rep session's own calls stay
    // scoped to its own id regardless.
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? skip, [FromQuery] int? take, [FromQuery] Guid? repId)
    {
        var scopeToRepId = User.GetRepIdOrNull() ?? repId;

        if (skip is null && take is null)
        {
            var all = await productService.GetAllAsync(User.GetEffectiveOwnerId(), scopeToRepId);
            return Ok(all);
        }

        var result = await productService.GetPagedAsync(
            User.GetEffectiveOwnerId(), skip ?? 0, Math.Clamp(take ?? 20, 1, 100), scopeToRepId);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await productService.GetByIdAsync(User.GetEffectiveOwnerId(), id, User.GetRepIdOrNull());
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

        var result = await productService.UpdateAsync(User.GetEffectiveOwnerId(), id, request, User.GetRepIdOrNull());
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

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await productService.DeleteAsync(User.GetEffectiveOwnerId(), id, User.GetRepIdOrNull());
        return NoContent();
    }

    // Read-only, same as GetAll (class-level "SalesRep,Rep") - without this,
    // ProductsRepository.sync()'s combined GET /products + GET /products/
    // archived would 403 on the second call for every Rep session, and
    // since both awaited calls fail together, the products list on that
    // device would never get past whatever was locally cached before -
    // the exact bug behind a Restricted rep still seeing the full catalog.
    [HttpGet("archived")]
    public async Task<IActionResult> GetArchived()
    {
        var result = await productService.GetArchivedAsync(User.GetEffectiveOwnerId(), User.GetRepIdOrNull());
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
