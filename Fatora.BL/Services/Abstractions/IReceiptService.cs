namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Responses;

public interface IReceiptService
{
    Task<List<ReceiptResponse>> GetAllAsync(Guid userId);
}
