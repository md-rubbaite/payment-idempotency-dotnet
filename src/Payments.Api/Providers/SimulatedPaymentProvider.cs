using System.Collections.Concurrent;
using Payments.Api.Domain;

namespace Payments.Api.Providers;

/// <summary>
/// Provider simulator with failure injection. Honours idempotency keys like Stripe or Adyen do.
/// Set <see cref="LoseNextResponse"/> to reproduce "charge succeeded, response lost".
/// </summary>
public sealed class SimulatedPaymentProvider : IPaymentProvider
{
    private readonly ConcurrentDictionary<string, ProviderCharge> _charges = new(StringComparer.Ordinal);
    private int _chargeCount;

    public bool LoseNextResponse { get; set; }

    public bool FailBeforeChargingNext { get; set; }

    /// <summary>Number of real charges made. A correct client never makes this exceed the number of distinct payments.</summary>
    public int ChargeCount => Volatile.Read(ref _chargeCount);

    public Task<ProviderCharge> ChargeAsync(string idempotencyKey, PaymentRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (FailBeforeChargingNext)
        {
            FailBeforeChargingNext = false;
            throw new TimeoutException("Provider unreachable (nothing was charged).");
        }

        var charge = _charges.GetOrAdd(idempotencyKey, _ =>
        {
            Interlocked.Increment(ref _chargeCount);
            var approved = request.AmountMinor <= 1_000_000;
            return new ProviderCharge($"ch_{Guid.NewGuid():N}", approved, approved ? null : "amount_limit_exceeded");
        });

        if (LoseNextResponse)
        {
            LoseNextResponse = false;
            throw new TimeoutException("Provider charged the card but the response was lost.");
        }

        return Task.FromResult(charge);
    }

    public Task<ProviderCharge?> FindChargeAsync(string idempotencyKey, CancellationToken ct) =>
        Task.FromResult(_charges.TryGetValue(idempotencyKey, out var c) ? c : null);
}
