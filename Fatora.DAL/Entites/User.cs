using Fatora.DAL.Entites;
using System.ComponentModel.DataAnnotations;

namespace Fatora.DAL.Entities;


public enum Role
{
    Admin,
    SalesRep
}

public enum SubscriptionType
{
    Trial,
    Monthly,
    Annual,
    Lifetime,

    // Duration in months is arbitrary, given by CustomMonths below - lets a
    // SubAdmin (see SubAdmin.CanActivateCustomMonths) or the Admin grant a
    // one-off length that doesn't match Monthly/Annual.
    Custom
}


public class User
{
    public Guid Id { get; set; }=Guid.NewGuid();

    public required string UserName { get; set; }
    public required string Password { get; set; }
    public required string Name { get; set; }
    public required string  PhoneNumber{ get; set; }
    public string? BusinessName{ get; set; }
    public string? LogoUrl { get; set; }
    public string? City { get; set; }
    public string? Street { get; set; }
    public Role Role { get; set; }=Role.SalesRep;
    public bool IsActive { get; set; } = true;

    // Explicit, owner-initiated switch (see AccountController's
    // enable-sales-manager action) - everything Rep-related on this
    // account's own side (rep management, the rep filter elsewhere) stays
    // hidden until this is true. Unrelated to the platform-level Admin/
    // SalesRep Role above - see Rep.cs for why these are separate concepts.
    public bool IsSalesManager { get; set; } = false;
    public string? BankName { get; set; }
    public string? AccountNumber { get; set; }
    public string? IBAN { get; set; }
    public int NextInvoiceNumber { get; set; } = 1;
    public int NextInvoiceNumberYear { get; set; }
    public SubscriptionType SubscriptionType { get; set; } = SubscriptionType.Trial;
    public DateTime SubscriptionStart { get; set; }
    public DateTime? SubscriptionEnd { get; set; }

    // Only meaningful when SubscriptionType is Custom - the month count that
    // SubscriptionEnd was computed from (see
    // UserService.ComputeSubscriptionEnd). Null and ignored for every other
    // SubscriptionType, cleared back to null the moment a later activation
    // switches away from Custom (see UpdateSubscriptionAsync) so a stale
    // value never lingers to confuse a later read.
    public int? CustomMonths { get; set; }

    // Which SubAdmin (if any) currently claims this subscriber, for
    // filtering/reporting only - never an access-control gate, since every
    // Admin/SubAdmin can always manage every subscriber regardless (see
    // UsersController). Set by scanning the subscriber's own QR
    // (SubAdminsController.ClaimSubscriber): a SubAdmin scan sets it to
    // itself, a top-Admin scan clears it back to null. Restrict, not
    // cascade, so a hidden/deleted SubAdmin's past attribution keeps
    // resolving - same reasoning as Order.CreatedByRepId.
    public Guid? ManagedBySubAdminId { get; set; }
    public SubAdmin? ManagedBySubAdmin { get; set; }
    public DateTime CreatedAt { get; set; }


    public List<Product> Items { get; set; } = new List<Product>();
    public List<Order> Orders { get; set; } = new List<Order>();
    public List<Customer> Customers { get; set; } = new List<Customer>();

}