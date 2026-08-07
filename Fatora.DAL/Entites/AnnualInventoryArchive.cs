using Fatora.DAL.Entities;

namespace Fatora.DAL.Entites;

// A permanent record of one "annual inventory" wipe (see
// AnnualInventoryService.RunAsync) - created atomically with the wipe itself,
// before the response is even returned, so the archive always exists for
// data that's just been deleted. PdfContent starts null: the PDF is rendered
// on the device from this run's own response (mobile owns PDF rendering, the
// backend has no PDF library), then uploaded back and attached here in a
// short follow-up call.
public class AnnualInventoryArchive
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User User { get; set; }

    public int Year { get; set; }

    public decimal TotalSales { get; set; }
    public decimal TotalCollected { get; set; }
    public decimal TotalRemainingDebt { get; set; }

    public required string CsvContent { get; set; }
    public byte[]? PdfContent { get; set; }

    public DateTime CreatedAt { get; set; }
}
