namespace Fatora.BL.DTOs.Responses;

public class OrderResponse
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhoneNumber { get; set; } = string.Empty;
    public string CustomerStreet { get; set; } = string.Empty;
    public string CustomerCity { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal Discount { get; set; }
    public string? Notes { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal Total { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingBalance { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new();
    public List<PaymentResponse> Payments { get; set; } = new();
}
