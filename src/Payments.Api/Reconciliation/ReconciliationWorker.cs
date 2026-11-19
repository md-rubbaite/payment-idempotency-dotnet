using Payments.Api.Domain;
using Payments.Api.Idempotency;
using Payments.Api.Providers;

namespace Payments.Api.Reconciliation;

/// <summary>
/// Background sweep that resolves uncertain payments without waiting for the client to retry.
/// It only looks up the provider's status; it never initiates a new charge on its own.
/// </summary>
public sealed class ReconciliationWorker(
    IIdempotencyStore store,
    IPaymentProvider provider,
    ILogger<ReconciliationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    public async Task<int> SweepOnceAsync(CancellationToken ct)
    {
        var resolved = 0;
        foreach (var record in await store.ListUncertainAsync(ct))
        {
            var charge = await provider.FindChargeAsync(record.Key, ct);
            if (charge is null)
            {
                // Leave it uncertain: the client's retry (same key) will charge safely.
                continue;
            }

            var status = charge.Approved ? PaymentStatus.Succeeded : PaymentStatus.Declined;
            await store.CompleteAsync(record.Key, new PaymentResult(PaymentIds.FromKey(record.Key), status, charge.ProviderReference, charge.DeclineReason), ct);
            resolved++;
        }

        if (resolved > 0)
        {
            Log.SweepResolved(logger, resolved);
        }

        return resolved;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.SweepFailed(logger, ex);
            }
        }
    }
}
