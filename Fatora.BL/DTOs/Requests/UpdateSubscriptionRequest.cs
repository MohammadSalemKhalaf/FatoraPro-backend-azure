namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;
using Fatora.DAL.Entities;

public sealed record UpdateSubscriptionRequest(
    [Required] SubscriptionType SubscriptionType
);
