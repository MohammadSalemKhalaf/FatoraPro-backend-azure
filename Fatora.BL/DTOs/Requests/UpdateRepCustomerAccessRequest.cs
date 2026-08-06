namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;
using Fatora.DAL.Entites;

public sealed record UpdateRepCustomerAccessRequest(
    [Required] CustomerAccessMode Mode,
    List<Guid> CustomerIds
);
