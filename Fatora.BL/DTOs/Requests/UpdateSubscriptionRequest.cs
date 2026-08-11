namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;
using Fatora.DAL.Entities;

public sealed record UpdateSubscriptionRequest(
    [Required] SubscriptionType SubscriptionType,
    int? CustomMonths = null,
    // Captured on the acting Admin/SubAdmin's device at the moment of
    // activation - see SubscriptionActivation.Latitude/Longitude. Null
    // when location permission was never granted or unavailable.
    double? Latitude = null,
    double? Longitude = null
);
