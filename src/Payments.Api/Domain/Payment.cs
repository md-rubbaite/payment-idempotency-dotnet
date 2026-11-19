namespace Payments.Api.Domain;

/// <summary>A request to charge a customer. Amounts are in minor units (e.g. cents) to avoid floating-point errors.</summary>
public sealed record PaymentRequest(string CustomerId, long AmountMinor, string Currency, string Description);

public enum PaymentStatus
{
    Succeeded,
    Declined,

    /// <summary>The provider may or may not have charged. Must be reconciled before any new charge attempt.</summary>
    Unknown,
}

public sealed record PaymentResult(string PaymentId, PaymentStatus Status, string? ProviderReference, string? Reason);
