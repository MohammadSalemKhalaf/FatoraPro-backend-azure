namespace Fatora.BL.Services.Abstractions;

using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;

public interface IPaymentService
{
    Task<PaymentResponse?> AddPaymentAsync(Guid userId, Guid orderId, CreatePaymentRequest request);
    Task<List<PaymentResponse>?> GetPaymentsAsync(Guid userId, Guid orderId);
    Task<bool> DeletePaymentAsync(Guid userId, Guid orderId, int paymentId);
}
