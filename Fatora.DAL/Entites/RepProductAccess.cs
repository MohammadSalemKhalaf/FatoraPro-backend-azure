namespace Fatora.DAL.Entites;

/// One product a Rep is allowed to see/sell, consulted only when that
/// Rep's ProductAccessMode is Restricted (Rep.cs) - ignored entirely in
/// the default All mode. Composite key (RepId, ProductId) - see
/// RepProductAccessConfiguration.
public class RepProductAccess
{
    public Guid RepId { get; set; }
    public Rep Rep { get; set; }

    public Guid ProductId { get; set; }
    public Product Product { get; set; }
}
