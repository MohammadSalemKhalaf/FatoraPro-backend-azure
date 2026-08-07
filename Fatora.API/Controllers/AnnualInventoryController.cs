using Fatora.API.Extensions;
using Fatora.BL.DTOs.Requests;
using Fatora.BL.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fatora.API.Controllers;

// Owner-only throughout, same as RepsController - a Rep session can never
// reach any of these, so there's no scopeToRepId concept here at all (see
// AnnualInventoryService.RunAsync).
[Route("api/annual-inventory")]
[ApiController]
[Authorize(Roles = "SalesRep")]
public class AnnualInventoryController(IAnnualInventoryService annualInventoryService) : ControllerBase
{
    [HttpPost("run")]
    public async Task<IActionResult> Run(RunAnnualInventoryRequest request)
    {
        var result = await annualInventoryService.RunAsync(User.GetUserId(), request);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, DeleteAnnualInventoryArchiveRequest request)
    {
        await annualInventoryService.DeleteArchiveAsync(User.GetUserId(), id, request.Password);
        return NoContent();
    }

    // A short follow-up to Run - the device renders the PDF locally from
    // Run's own response, then uploads it here to attach to that same
    // archive (see AnnualInventoryArchive.PdfContent).
    [HttpPost("{id:guid}/pdf")]
    public async Task<IActionResult> AttachPdf(Guid id, IFormFile file)
    {
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        var result = await annualInventoryService.AttachPdfAsync(User.GetUserId(), id, stream.ToArray());
        return Ok(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await annualInventoryService.GetAllAsync(User.GetUserId());
        return Ok(result);
    }

    [HttpGet("{id:guid}/csv")]
    public async Task<IActionResult> DownloadCsv(Guid id)
    {
        var (csvContent, year) = await annualInventoryService.GetCsvAsync(User.GetUserId(), id);
        var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csvContent)).ToArray();
        return File(bytes, "text/csv", $"annual-inventory-{year}.csv");
    }

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> DownloadPdf(Guid id)
    {
        var (pdfContent, year) = await annualInventoryService.GetPdfAsync(User.GetUserId(), id);
        return File(pdfContent, "application/pdf", $"annual-inventory-{year}.pdf");
    }
}
