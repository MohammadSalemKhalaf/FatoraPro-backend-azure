namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

public interface IAnnualInventoryService
{
    Task<RunAnnualInventoryResponse> RunAsync(Guid userId, RunAnnualInventoryRequest request);
    Task DeleteArchiveAsync(Guid userId, Guid archiveId, string password);
    Task<AnnualInventoryArchiveResponse> AttachPdfAsync(Guid userId, Guid archiveId, byte[] pdfBytes);
    Task<List<AnnualInventoryArchiveResponse>> GetAllAsync(Guid userId);
    Task<(string CsvContent, int Year)> GetCsvAsync(Guid userId, Guid archiveId);
    Task<(byte[] PdfContent, int Year)> GetPdfAsync(Guid userId, Guid archiveId);
}
