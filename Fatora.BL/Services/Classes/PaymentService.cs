using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;

namespace Fatora.BL.Services.Classes;

public class PaymentService(AppDbContext dbContext) : IPaymentService
{
    public async Task<PaymentResponse?> AddPaymentAsync(Guid userId, Guid orderId, CreatePaymentRequest request)
    {
        var order = await FindOwnedOrder(userId, orderId);

        if (order is null)
        {
            return null;
        }

        if (request.Amount > order.RemainingBalance)
        {
            throw new InvalidOperationException($"Payment amount ({request.Amount}) exceeds the remaining balance ({order.RemainingBalance}).");
        }

        var payment = new Payment
        {
            OrderId = order.Id,
            Amount = request.Amount,
            Notes = request.Notes
        };

        dbContext.Payments.Add(payment);
        await dbContext.SaveChangesAsync();

        return ToResponse(payment);
    }

    public async Task<List<PaymentResponse>?> GetPaymentsAsync(Guid userId, Guid orderId)
    {
        var order = await FindOwnedOrder(userId, orderId);
        return order?.Payments.Select(ToResponse).ToList();
    }

    public async Task<bool> DeletePaymentAsync(Guid userId, Guid orderId, int paymentId)
    {
        var order = await FindOwnedOrder(userId, orderId);
        var payment = order?.Payments.FirstOrDefault(p => p.Id == paymentId);

        if (payment is null)
        {
            return false;
        }

        dbContext.Payments.Remove(payment);
        await dbContext.SaveChangesAsync();

        return true;
    }

    private async Task<Order?> FindOwnedOrder(Guid userId, Guid orderId) =>
        await dbContext.Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId);

    private static PaymentResponse ToResponse(Payment payment) => new()
    {
        Id = payment.Id,
        Amount = payment.Amount,
        PaidAt = payment.PaidAt,
        Notes = payment.Notes
    };
}
