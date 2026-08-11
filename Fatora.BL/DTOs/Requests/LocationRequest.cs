namespace Fatora.BL.DTOs.Requests;

/// Optional device location at the moment of an action with no other
/// request body of its own - see UsersController.Suspend/Activate. Both
/// null is a normal, expected case (permission never granted, or the
/// device was offline), never a validation failure.
public sealed record LocationRequest(double? Latitude = null, double? Longitude = null);
