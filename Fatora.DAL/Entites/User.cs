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
    Lifetime
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
    public DateTime CreatedAt { get; set; }


    public List<Product> Items { get; set; } = new List<Product>();
    public List<Order> Orders { get; set; } = new List<Order>();
    public List<Customer> Customers { get; set; } = new List<Customer>();

}