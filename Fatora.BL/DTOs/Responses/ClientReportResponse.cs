namespace Fatora.BL.DTOs.Responses;

public class ClientReportResponse
{
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int InvoiceCount { get; set; }
    public decimal TotalValue { get; set; }
}
