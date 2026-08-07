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
}
