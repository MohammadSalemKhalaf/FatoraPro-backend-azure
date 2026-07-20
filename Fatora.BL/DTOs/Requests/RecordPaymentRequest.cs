namespace Fatora.BL.DTOs.Requests;

using System.ComponentModel.DataAnnotations;

public sealed record RecordPaymentRequest(
    [Range(0.01, double.MaxValue)] decimal Amount
);
