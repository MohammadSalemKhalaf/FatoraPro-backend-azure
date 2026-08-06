namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;
using Fatora.DAL.Entites;

public sealed record UpdateRepProductAccessRequest(
    [Required] ProductAccessMode Mode,
    List<Guid> ProductIds
);
