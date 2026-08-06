namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;
using Fatora.DAL.Entites;

public sealed record UpdateRepProductAccessRequest(
    [Required] AccessMode Mode,
    List<Guid> ProductIds
);
