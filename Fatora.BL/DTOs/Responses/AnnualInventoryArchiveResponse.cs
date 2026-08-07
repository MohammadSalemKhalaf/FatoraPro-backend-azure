namespace Fatora.BL.DTOs.Responses;

public class AnnualInventoryArchiveResponse
{
    public Guid Id { get; set; }
    public int Year { get; set; }
    public decimal TotalSales { get; set; }
    public decimal TotalCollected { get; set; }
    public decimal TotalRemainingDebt { get; set; }
    public bool HasPdf { get; set; }
    public DateTime CreatedAt { get; set; }
}

// The run response only - carries the CSV text the device needs immediately
// to render/share it (and to render the PDF from these same totals), unlike
// the plain list/summary response above which would otherwise ship a
// potentially large string for every row just to draw a list.
public class RunAnnualInventoryResponse : AnnualInventoryArchiveResponse
{
    public required string CsvContent { get; set; }
    public List<CustomerSummaryResponse> CustomerSummaries { get; set; } = [];
}

// One row per customer with at least one invoice - drives the PDF's
// per-customer table. Not persisted on the archive itself: the PDF that's
// rendered from this is what gets permanently stored, so there's nothing
// to keep this data around for once that upload completes.
public class CustomerSummaryResponse
{
    public required string Name { get; set; }
    public decimal TotalInvoiced { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalRemaining { get; set; }
}
