namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

public interface IOrderService
{
    Task<OrderResponse> CreateAsync(Guid userId, CreateOrderRequest request, Guid? createdByRepId = null);
    Task<List<OrderResponse>> GetAllAsync(Guid userId, Guid? scopeToRepId = null);
    Task<List<OrderResponse>> GetPagedAsync(Guid userId, int skip, int take, Guid? scopeToRepId = null);
    Task<OrderResponse> GetByIdAsync(Guid userId, Guid id, Guid? scopeToRepId = null);
    Task<OrderResponse> UpdateAsync(Guid userId, Guid id, UpdateOrderRequest request, Guid? scopeToRepId = null);
    Task DeleteAsync(Guid userId, Guid id, Guid? scopeToRepId = null);
    Task<OrderResponse> ReturnAsync(Guid userId, Guid id, Guid? scopeToRepId = null);
    Task<OrderResponse> RecordPaymentAsync(Guid userId, Guid id, RecordPaymentRequest request, Guid? scopeToRepId = null);
    Task<OrderSummaryResponse> GetSummaryAsync(Guid userId, SummaryPeriod period);
}
