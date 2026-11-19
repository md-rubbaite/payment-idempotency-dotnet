using Payments.Api.Domain;
using Payments.Api.Idempotency;
using Payments.Api.Providers;

namespace Payments.Api;

public abstract record ProcessOutcome
{
    private ProcessOutcome() { }

    public sealed record Done(PaymentResult Result, bool Replayed) : ProcessOutcome;

    public sealed record InProgress : ProcessOutcome;

    public sealed record KeyReusedWithDifferentRequest : ProcessOutcome;
}

/// <summary>
/// Charges a customer at most once per idempotency key, including when the provider
/// succeeds but its response is lost, when clients retry concurrently, and when a worker crashes.
/// </summary>
public sealed class PaymentService(IIdempotencyStore store, IPaymentProvider provider, ILogger<PaymentService> logger)
{
    internal static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    public async Task<ProcessOutcome> ProcessAsync(string idempotencyKey, PaymentRequest request, CancellationToken ct)
    {
        var fingerprint = RequestFingerprint.Compute(request);
        var begin = await store.TryBeginAsync(idempotencyKey, fingerprint, Lease, ct);

        switch (begin)
        {
            case BeginOutcome.Replay replay:
                return new ProcessOutcome.Done(replay.Result, Replayed: true);
            case BeginOutcome.Conflict:
                return new ProcessOutcome.InProgress();
            case BeginOutcome.FingerprintMismatch:
                return new ProcessOutcome.KeyReusedWithDifferentRequest();
            case BeginOutcome.NeedsReconciliation:
                // Never charge again blindly: ask the provider what actually happened first.
                var reconciled = await ReconcileAsync(idempotencyKey, request, ct);
                return new ProcessOutcome.Done(reconciled, Replayed: true);
            case BeginOutcome.Acquired:
                return new ProcessOutcome.Done(await ChargeAsync(idempotencyKey, request, ct), Replayed: false);
            default:
                throw new InvalidOperationException($"Unhandled outcome {begin}");
        }
    }

    /// <summary>Resolves an uncertain payment using the provider as the source of truth.</summary>
    public async Task<PaymentResult> ReconcileAsync(string idempotencyKey, PaymentRequest request, CancellationToken ct)
    {
        ProviderCharge? charge;
        try
        {
            charge = await provider.FindChargeAsync(idempotencyKey, ct);
        }
        catch (TimeoutException)
        {
            return Uncertain(idempotencyKey);
        }

        if (charge is not null)
        {
            var result = ToResult(idempotencyKey, charge);
            await store.CompleteAsync(idempotencyKey, result, ct);
            Log.ReconciledExisting(logger, idempotencyKey, result.Status);
            return result;
        }

        // The provider never saw the charge, so retrying with the same key is safe.
        Log.ReconciledMissing(logger, idempotencyKey);
        return await ChargeAsync(idempotencyKey, request, ct);
    }

    private async Task<PaymentResult> ChargeAsync(string key, PaymentRequest request, CancellationToken ct)
    {
        try
        {
            var charge = await provider.ChargeAsync(key, request, ct);
            var result = ToResult(key, charge);
            await store.CompleteAsync(key, result, ct);
            return result;
        }
        catch (TimeoutException ex)
        {
            Log.OutcomeUnknown(logger, ex, key);
            await store.MarkUncertainAsync(key, ct);
            return Uncertain(key);
        }
    }

    private static PaymentResult Uncertain(string key) =>
        new(PaymentId(key), PaymentStatus.Unknown, null, "outcome_unknown_reconciliation_pending");

    private static PaymentResult ToResult(string key, ProviderCharge charge) =>
        new(PaymentId(key), charge.Approved ? PaymentStatus.Succeeded : PaymentStatus.Declined, charge.ProviderReference, charge.DeclineReason);

    private static string PaymentId(string key) => PaymentIds.FromKey(key);
}
