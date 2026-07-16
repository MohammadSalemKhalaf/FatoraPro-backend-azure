namespace Fatora.BL.DTOs.Responses;

public class PaymentResponse
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; }
    public string? Notes { get; set; }
}
