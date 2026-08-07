namespace Fatora.DAL.Entites;

public enum SupportContactType
{
    Phone,
    WhatsApp,
    Email
}

/// A platform-wide (not per-tenant) support channel the Admin configures -
/// replaces the old hardcoded support_contact.dart constant. Surfaced both
/// to the Admin (full CRUD) and publicly to every tenant/subscriber
/// (read-only, active rows only - see SupportContactsController).
public class SupportContact
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public SupportContactType Type { get; set; }

    // The raw phone number / WhatsApp number / email address - format
    // depends on Type, validated client-side per type rather than here.
    public required string Value { get; set; }

    // Optional display text (e.g. "الدعم الفني", "المبيعات") - falls back
    // to a per-Type default label on the frontend when null.
    public string? Label { get; set; }

    // Lower sorts first - set via SupportContactService.ReorderAsync.
    public int DisplayOrder { get; set; }

    // Lets the Admin temporarily hide a channel (e.g. outside working
    // hours) without losing its configured value - the public endpoint
    // only ever returns IsActive rows.
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
}
