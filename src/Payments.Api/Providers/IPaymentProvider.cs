using Payments.Api.Domain;

namespace Payments.Api.Providers;

public sealed record ProviderCharge(string ProviderReference, bool Approved, string? DeclineReason);

/// <summary>
/// External payment provider. The idempotency key is forwarded so the provider itself deduplicates:
/// a local database transaction cannot make a remote charge atomic.
/// </summary>
public interface IPaymentProvider
{
    /// <exception cref="TimeoutException">The provider did not answer in time. The charge may still have happened.</exception>
    Task<ProviderCharge> ChargeAsync(string idempotencyKey, PaymentRequest request, CancellationToken ct);

    /// <summary>Authoritative lookup by idempotency key. Returns null when the provider never received the charge.</summary>
    Task<ProviderCharge?> FindChargeAsync(string idempotencyKey, CancellationToken ct);
}
