namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

public interface IOrderService
{
    Task<OrderResponse> CreateAsync(Guid userId, CreateOrderRequest request);
    Task<List<OrderResponse>> GetAllAsync(Guid userId);
    Task<OrderResponse> GetByIdAsync(Guid userId, Guid id);
    Task<OrderResponse> UpdateAsync(Guid userId, Guid id, UpdateOrderRequest request);
    Task DeleteAsync(Guid userId, Guid id);
    Task<OrderResponse> RecordPaymentAsync(Guid userId, Guid id, RecordPaymentRequest request);
    Task<OrderSummaryResponse> GetSummaryAsync(Guid userId, SummaryPeriod period);
}
