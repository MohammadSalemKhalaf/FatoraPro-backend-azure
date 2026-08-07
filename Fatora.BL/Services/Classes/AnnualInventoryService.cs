using Fatora.BL.DTOs.Requests;
using Fatora.BL.DTOs.Responses;
using Fatora.BL.Exceptions;
using Fatora.BL.Services.Abstractions;
using Fatora.DAL.Data;
using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Fatora.BL.Services.Classes;

// A one-way, owner-only "close the books and start fresh" action - unlike
// OrderService.DeleteAsync (a soft, purely operational hide), this genuinely
// removes the selected data after archiving it, so the two exported files
// become the only remaining record. See Order.IsDeleted's comment for the
// contrast.
public class AnnualInventoryService(AppDbContext dbContext, IPasswordHasherService passwordHasher)
    : IAnnualInventoryService
{
    private const decimal DebtTolerance = 0.01m;

    public async Task<RunAnnualInventoryResponse> RunAsync(Guid userId, RunAnnualInventoryRequest request)
    {
        var user = await dbContext.Users.FirstAsync(u => u.Id == userId);

        if (!passwordHasher.Verify(user, request.Password, user.Password))
        {
            throw new UnauthorizedException("Incorrect password.");
        }

        if (!request.IncludeCustomers && !request.IncludeItems && !request.IncludeInvoices)
        {
            throw new BadRequestException("Select at least one category to include.");
        }

        // Customers and Items both force Invoices along with them for the
        // *wipe* - a deleted customer can't leave dangling invoices, and
        // neither can a deleted product leave dangling invoice lines. This
        // only decides what gets removed below; the archive itself is
        // always a complete snapshot of everything regardless (see
        // BuildCsv) - it's the account's permanent closing record, not just
        // a receipt for whichever boxes were checked.
        var includeInvoices = request.IncludeInvoices || request.IncludeCustomers || request.IncludeItems;

        var orders = await dbContext.Orders
            .Include(o => o.Customer)
            .Where(o => o.UserId == userId)
            .ToListAsync();
        var customers = await dbContext.Customers.Where(c => c.UserId == userId).ToListAsync();
        var products = await dbContext.Products.Where(p => p.UserId == userId).ToListAsync();
        var receipts = await dbContext.Receipts.Where(r => r.UserId == userId && r.IsActive).ToListAsync();

        var totalSales = orders.Where(o => !o.IsReturned).Sum(o => o.Total);
        var totalCollected = orders.Sum(o => o.PaidAmount);
        var totalRemainingDebt = orders.Sum(o => o.RemainingBalance);

        var csvContent = BuildCsv(totalSales, totalCollected, totalRemainingDebt, orders, customers, products, receipts);
        var customerSummaries = BuildCustomerSummaries(orders);

        // Deletion order matters: Orders (and their OrderItems, cascaded)
        // always go first whenever Invoices is effectively included, so
        // Products can never be removed while an OrderItem elsewhere might
        // still reference them - Product -> OrderItem is itself a cascade
        // delete, and removing a Product while unrelated live orders still
        // used it would silently corrupt their line items too.
        if (includeInvoices)
        {
            dbContext.Orders.RemoveRange(orders);
        }

        if (request.IncludeCustomers)
        {
            // Cascades each customer's Receipts - Orders are already gone
            // above whenever this branch can run (Customers always forces
            // Invoices).
            dbContext.Customers.RemoveRange(customers);
        }

        if (request.IncludeItems)
        {
            dbContext.Products.RemoveRange(products);
        }

        var archive = new AnnualInventoryArchive
        {
            UserId = userId,
            Year = DateTime.UtcNow.Year,
            TotalSales = totalSales,
            TotalCollected = totalCollected,
            TotalRemainingDebt = totalRemainingDebt,
            CsvContent = csvContent,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.AnnualInventoryArchives.Add(archive);

        // One SaveChanges - the wipe and the archive that records it commit
        // together, so there's never a moment where data was removed
        // without the archive that's supposed to be its replacement.
        await dbContext.SaveChangesAsync();

        // A separate, follow-up pass (not part of the transaction above): a
        // hidden rep (see Rep.IsHidden / RepService.DeleteAsync) can only
        // ever be hard-deleted once nothing references it any more, and this
        // wipe having just run is the one moment that's actually likely to
        // become true. Safe to run on every call regardless of which boxes
        // were checked - it's a pure "if now-orphaned, clean it up" pass, a
        // no-op whenever there's nothing eligible.
        await HardDeleteOrphanedHiddenRepsAsync(userId);

        return new RunAnnualInventoryResponse
        {
            Id = archive.Id,
            Year = archive.Year,
            TotalSales = archive.TotalSales,
            TotalCollected = archive.TotalCollected,
            TotalRemainingDebt = archive.TotalRemainingDebt,
            HasPdf = false,
            CreatedAt = archive.CreatedAt,
            CsvContent = archive.CsvContent,
            CustomerSummaries = customerSummaries
        };
    }

    // Deletes the archive record itself, not the account data it recorded -
    // that data is already long gone by the time an archive exists at all
    // (see RunAsync). Password-gated the same way RunAsync is, since this
    // permanently destroys the one remaining record of a past wipe (its CSV/
    // PDF are stored directly on this row - see AnnualInventoryArchive).
    public async Task DeleteArchiveAsync(Guid userId, Guid archiveId, string password)
    {
        var user = await dbContext.Users.FirstAsync(u => u.Id == userId);
        if (!passwordHasher.Verify(user, password, user.Password))
        {
            throw new UnauthorizedException("Incorrect password.");
        }

        var archive = await FindOwnedArchive(userId, archiveId);
        dbContext.AnnualInventoryArchives.Remove(archive);
        await dbContext.SaveChangesAsync();
    }

    public async Task<AnnualInventoryArchiveResponse> AttachPdfAsync(Guid userId, Guid archiveId, byte[] pdfBytes)
    {
        var archive = await FindOwnedArchive(userId, archiveId);
        archive.PdfContent = pdfBytes;
        await dbContext.SaveChangesAsync();
        return ToResponse(archive);
    }

    public async Task<List<AnnualInventoryArchiveResponse>> GetAllAsync(Guid userId)
    {
        var archives = await dbContext.AnnualInventoryArchives
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        return archives.Select(ToResponse).ToList();
    }

    public async Task<(string CsvContent, int Year)> GetCsvAsync(Guid userId, Guid archiveId)
    {
        var archive = await FindOwnedArchive(userId, archiveId);
        return (archive.CsvContent, archive.Year);
    }

    public async Task<(byte[] PdfContent, int Year)> GetPdfAsync(Guid userId, Guid archiveId)
    {
        var archive = await FindOwnedArchive(userId, archiveId);
        if (archive.PdfContent is null)
        {
            throw new NotFoundException(nameof(AnnualInventoryArchive.PdfContent), archiveId);
        }
        return (archive.PdfContent, archive.Year);
    }

    // A hidden rep (Rep.IsHidden, set by RepService.DeleteAsync) is only
    // ever hard-deleted once nothing references it any more -
    // CreatedByRepId on Order/Customer/Product is a Restrict FK precisely
    // so this can never run early and orphan a still-live record (see
    // OrderConfiguration.CreatedByRep and friends). Receipts are checked
    // too even though this service never deletes them itself - a rep that
    // ever recorded one stays hidden-but-present indefinitely rather than
    // ever silently losing that receipt's attribution.
    private async Task HardDeleteOrphanedHiddenRepsAsync(Guid userId)
    {
        var hiddenReps = await dbContext.Reps
            .Where(r => r.OwnerUserId == userId && r.IsHidden)
            .ToListAsync();
        if (hiddenReps.Count == 0)
        {
            return;
        }

        foreach (var rep in hiddenReps)
        {
            var stillReferenced =
                await dbContext.Orders.AnyAsync(o => o.CreatedByRepId == rep.Id) ||
                await dbContext.Customers.AnyAsync(c => c.CreatedByRepId == rep.Id) ||
                await dbContext.Products.AnyAsync(p => p.CreatedByRepId == rep.Id) ||
                await dbContext.Receipts.AnyAsync(r => r.CreatedByRepId == rep.Id);

            if (!stillReferenced)
            {
                dbContext.Reps.Remove(rep);
            }
        }

        await dbContext.SaveChangesAsync();
    }

    private async Task<AnnualInventoryArchive> FindOwnedArchive(Guid userId, Guid archiveId)
    {
        var archive = await dbContext.AnnualInventoryArchives
            .FirstOrDefaultAsync(a => a.Id == archiveId && a.UserId == userId);

        if (archive is null)
        {
            throw new NotFoundException(nameof(AnnualInventoryArchive), archiveId);
        }

        return archive;
    }

    private static AnnualInventoryArchiveResponse ToResponse(AnnualInventoryArchive archive) => new()
    {
        Id = archive.Id,
        Year = archive.Year,
        TotalSales = archive.TotalSales,
        TotalCollected = archive.TotalCollected,
        TotalRemainingDebt = archive.TotalRemainingDebt,
        HasPdf = archive.PdfContent is not null,
        CreatedAt = archive.CreatedAt
    };

    // One row per customer who has at least one invoice - drives the PDF's
    // per-customer table. Mirrors the same effective-remaining rule as the
    // CSV (a covered-by-receipt invoice counts as settled, not outstanding).
    private static List<CustomerSummaryResponse> BuildCustomerSummaries(List<Order> orders) =>
        orders
            .GroupBy(o => o.CustomerId)
            .Select(g => new CustomerSummaryResponse
            {
                Name = g.First().Customer.Name,
                TotalInvoiced = g.Where(o => !o.IsReturned).Sum(o => o.Total),
                TotalPaid = g.Sum(o => o.PaidAmount),
                TotalRemaining = g.Sum(o => EffectiveRemaining(o))
            })
            .OrderByDescending(c => c.TotalInvoiced)
            .ToList();

    // coveredByReceipt is a separate flag from status (see
    // Order.CoveredByReceipt) - an administrative write-off leaves
    // RemainingBalance itself untouched, so both the CSV and the PDF's
    // per-customer table have to zero it out the same way the app's own UI
    // (a strikethrough) and the existing data-export feature already do.
    private static decimal EffectiveRemaining(Order order) =>
        !order.IsReturned && order.CoveredByReceipt ? 0 : order.RemainingBalance;

    private static string StatusLabel(Order order, DateOnly today)
    {
        if (!order.IsReturned && order.CoveredByReceipt) return "مغطاة بقبضة";
        return OrderService.ComputeStatus(order, today) switch
        {
            "Paid" => "مدفوعة",
            "Overdue" => "متأخرة",
            "PartiallyPaid" => "مدفوعة جزئيًا",
            "Returned" => "مرتجعة",
            _ => "مُرسلة"
        };
    }

    // Always a complete snapshot of the whole account at the moment of the
    // run - customers/items/invoices all included regardless of which boxes
    // were actually checked for deletion (see RunAsync) - and structured to
    // match the app's existing DataExportService.buildCsv on the frontend
    // exactly (same section headers/columns/quoting), so a downloaded
    // annual archive reads consistently with the everyday export feature
    // instead of being its own, unfamiliar format.
    private static string BuildCsv(
        decimal totalSales, decimal totalCollected, decimal totalRemainingDebt,
        List<Order> orders, List<Customer> customers, List<Product> products, List<Receipt> receipts)
    {
        var sb = new StringBuilder();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        AppendRow(sb, "ملخص الجرد السنوي");
        AppendRow(sb, "إجمالي المبيعات", "إجمالي المقبوض", "إجمالي الدين المتبقي");
        AppendRow(sb, totalSales.ToString("F2"), totalCollected.ToString("F2"), totalRemainingDebt.ToString("F2"));
        sb.Append("\r\n");

        var returned = orders.Where(o => o.IsReturned).ToList();
        var active = orders.Where(o => !o.IsReturned).ToList();

        if (returned.Count > 0)
        {
            AppendRow(sb, "الفواتير المرتجعة");
            AppendRow(sb, "رقم الفاتورة", "العميل", "رقم الهاتف", "تاريخ الإصدار", "القيمة الأصلية");
            decimal returnedTotal = 0;
            foreach (var order in returned)
            {
                returnedTotal += order.Total;
                AppendRow(sb, order.InvoiceNumber, order.Customer.Name, order.Customer.PhoneNumber ?? "",
                    order.CreatedAt.ToString("yyyy-MM-dd"), order.Total.ToString("F2"));
            }
            AppendRow(sb, "الإجمالي العام", "", "", "", returnedTotal.ToString("F2"));
            sb.Append("\r\n");
        }

        AppendRow(sb, "الفواتير");
        AppendRow(sb, "رقم الفاتورة", "العميل", "رقم الهاتف", "تاريخ الإصدار", "تاريخ الاستحقاق",
            "الحالة", "الخصم", "الإجمالي", "المدفوع", "المتبقي");
        decimal discountTotal = 0, amountTotal = 0, paidTotal = 0, remainingTotal = 0;
        foreach (var order in active)
        {
            var remaining = EffectiveRemaining(order);
            discountTotal += order.Discount;
            amountTotal += order.Total;
            paidTotal += order.PaidAmount;
            remainingTotal += remaining;
            AppendRow(sb, order.InvoiceNumber, order.Customer.Name, order.Customer.PhoneNumber ?? "",
                order.CreatedAt.ToString("yyyy-MM-dd"),
                order.DueDate?.ToString("yyyy-MM-dd") ?? "",
                StatusLabel(order, today),
                order.Discount.ToString("F2"), order.Total.ToString("F2"),
                order.PaidAmount.ToString("F2"), remaining.ToString("F2"));
        }
        AppendRow(sb, "الإجمالي العام", "", "", "", "", "", discountTotal.ToString("F2"),
            amountTotal.ToString("F2"), paidTotal.ToString("F2"), remainingTotal.ToString("F2"));
        sb.Append("\r\n");

        AppendDebtsSection(sb, orders, receipts);

        AppendRow(sb, "العملاء");
        AppendRow(sb, "الاسم", "اسم المتجر", "رقم الهاتف", "الشارع", "المدينة", "تاريخ الإضافة");
        foreach (var customer in customers)
        {
            AppendRow(sb, customer.Name, customer.StoreName ?? "", customer.PhoneNumber ?? "",
                customer.Street ?? "", customer.City ?? "", customer.CreatedAt.ToString("yyyy-MM-dd"));
        }
        sb.Append("\r\n");

        AppendRow(sb, "الأصناف");
        AppendRow(sb, "الاسم", "الوصف", "سعر الشراء", "سعر البيع");
        foreach (var product in products)
        {
            AppendRow(sb, product.Name, product.Description ?? "",
                product.PurchasePrice.ToString("F2"), product.SellPrice.ToString("F2"));
        }

        return sb.ToString();
    }

    // Nets each customer's outstanding balance against their general
    // receipts (a payment against the customer's total debt, not any one
    // invoice - see Receipt.cs), floored at 0 - mirrors DataExportService's
    // "ديون العملاء" section on the frontend exactly.
    private static void AppendDebtsSection(StringBuilder sb, List<Order> orders, List<Receipt> receipts)
    {
        var receivedByCustomer = receipts
            .GroupBy(r => r.CustomerId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));

        var debts = orders
            .Where(o => !o.CoveredByReceipt && o.RemainingBalance > DebtTolerance)
            .GroupBy(o => o.CustomerId)
            .Select(g =>
            {
                var received = receivedByCustomer.GetValueOrDefault(g.Key, 0m);
                var netAmount = Math.Max(0, g.Sum(o => o.RemainingBalance) - received);
                return new
                {
                    Name = g.First().Customer.Name,
                    Phone = g.First().Customer.PhoneNumber ?? "",
                    UnpaidInvoiceCount = g.Count(),
                    Amount = netAmount
                };
            })
            .Where(d => d.Amount > DebtTolerance)
            .OrderByDescending(d => d.Amount)
            .ToList();

        AppendRow(sb, "ديون العملاء");
        AppendRow(sb, "العميل", "رقم الهاتف", "عدد الفواتير غير المسددة", "إجمالي المستحق");
        decimal totalDebt = 0;
        foreach (var debt in debts)
        {
            totalDebt += debt.Amount;
            AppendRow(sb, debt.Name, debt.Phone, debt.UnpaidInvoiceCount.ToString(), debt.Amount.ToString("F2"));
        }
        AppendRow(sb, "الإجمالي العام", "", "", totalDebt.ToString("F2"));
        sb.Append("\r\n");
    }

    private static void AppendRow(StringBuilder sb, params string[] fields)
    {
        sb.Append(string.Join(",", fields.Select(f => $"\"{f.Replace("\"", "\"\"")}\"")));
        sb.Append("\r\n");
    }
}
