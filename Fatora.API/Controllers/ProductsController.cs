using Fatora.API.Extensions;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "SalesRep")]
public class ProductsController(IProductService productService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateProductRequest request)
    {
        var result = await productService.CreateAsync(User.GetUserId(), request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await productService.GetAllAsync(User.GetUserId());
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await productService.GetByIdAsync(User.GetUserId(), id);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateProductRequest request)
    {
        var result = await productService.UpdateAsync(User.GetUserId(), id, request);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await productService.DeleteAsync(User.GetUserId(), id);
        return deleted ? NoContent() : NotFound();
    }
}
