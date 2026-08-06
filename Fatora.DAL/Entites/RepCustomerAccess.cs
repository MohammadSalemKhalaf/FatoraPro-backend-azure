namespace Fatora.DAL.Entites;

/// One customer a Rep is allowed to see/invoice, consulted only when that
/// Rep's CustomerAccessMode is Restricted (Rep.cs) - ignored entirely in
/// Own/All modes. Composite key (RepId, CustomerId) - see
/// RepCustomerAccessConfiguration.
public class RepCustomerAccess
{
    public Guid RepId { get; set; }
    public Rep Rep { get; set; }

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; }
}
