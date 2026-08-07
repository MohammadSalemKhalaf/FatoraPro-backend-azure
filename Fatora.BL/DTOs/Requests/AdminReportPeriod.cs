namespace Fatora.BL.DTOs.Requests;

// Mirrors the admin frontend's own ReportPeriod (report_models.dart) -
// distinct from the tenant-side SummaryPeriod (Week/Month/Year/All, no
// Today), since this serves a different screen with a different filter set.
public enum AdminReportPeriod
{
    Today,
    Week,
    Month,
    Year,
    All
}
