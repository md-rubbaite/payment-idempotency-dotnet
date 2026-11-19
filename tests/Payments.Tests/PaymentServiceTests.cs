using Microsoft.Extensions.Logging.Abstractions;
using Payments.Api;
using Payments.Api.Domain;
using Payments.Api.Idempotency;
using Payments.Api.Providers;
using Payments.Api.Reconciliation;

namespace Payments.Tests;

public sealed class PaymentServiceTests
{
    private static readonly PaymentRequest Request = new("cus_42", 2_500, "USD", "Order 1001");

    private readonly SimulatedPaymentProvider _provider = new();
    private readonly InMemoryIdempotencyStore _store = new(TimeProvider.System);
    private readonly PaymentService _service;

    public PaymentServiceTests() =>
        _service = new PaymentService(_store, _provider, NullLogger<PaymentService>.Instance);

    [Fact]
    public async Task First_request_charges_once()
    {
        var outcome = await _service.ProcessAsync("key-1", Request, CancellationToken.None);

        var done = Assert.IsType<ProcessOutcome.Done>(outcome);
        Assert.Equal(PaymentStatus.Succeeded, done.Result.Status);
        Assert.False(done.Replayed);
        Assert.Equal(1, _provider.ChargeCount);
    }

    [Fact]
    public async Task Retry_with_same_key_returns_the_original_result_without_charging_again()
    {
        var first = (ProcessOutcome.Done)await _service.ProcessAsync("key-1", Request, CancellationToken.None);
        var retry = (ProcessOutcome.Done)await _service.ProcessAsync("key-1", Request, CancellationToken.None);

        Assert.True(retry.Replayed);
        Assert.Equal(first.Result, retry.Result);
        Assert.Equal(1, _provider.ChargeCount);
    }

    [Fact]
    public async Task Same_key_with_a_different_body_is_rejected()
    {
        await _service.ProcessAsync("key-1", Request, CancellationToken.None);

        var outcome = await _service.ProcessAsync("key-1", Request with { AmountMinor = 9_999 }, CancellationToken.None);

        Assert.IsType<ProcessOutcome.KeyReusedWithDifferentRequest>(outcome);
        Assert.Equal(1, _provider.ChargeCount);
    }

    [Fact]
    public async Task Lost_response_then_client_retry_reconciles_instead_of_double_charging()
    {
        _provider.LoseNextResponse = true;

        var first = (ProcessOutcome.Done)await _service.ProcessAsync("key-1", Request, CancellationToken.None);
        Assert.Equal(PaymentStatus.Unknown, first.Result.Status);

        var retry = (ProcessOutcome.Done)await _service.ProcessAsync("key-1", Request, CancellationToken.None);

        Assert.Equal(PaymentStatus.Succeeded, retry.Result.Status);
        Assert.NotNull(retry.Result.ProviderReference);
        Assert.Equal(1, _provider.ChargeCount);
    }

    [Fact]
    public async Task Timeout_before_charge_then_retry_charges_exactly_once()
    {
        _provider.FailBeforeChargingNext = true;

        var first = (ProcessOutcome.Done)await _service.ProcessAsync("key-1", Request, CancellationToken.None);
        Assert.Equal(PaymentStatus.Unknown, first.Result.Status);
        Assert.Equal(0, _provider.ChargeCount);

        var retry = (ProcessOutcome.Done)await _service.ProcessAsync("key-1", Request, CancellationToken.None);

        Assert.Equal(PaymentStatus.Succeeded, retry.Result.Status);
        Assert.Equal(1, _provider.ChargeCount);
    }

    [Fact]
    public async Task Concurrent_duplicates_produce_a_single_charge()
    {
        var tasks = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => _service.ProcessAsync("key-1", Request, CancellationToken.None)))
            .ToArray();

        var outcomes = await Task.WhenAll(tasks);

        Assert.Equal(1, _provider.ChargeCount);
        Assert.All(outcomes, o => Assert.True(o is ProcessOutcome.Done or ProcessOutcome.InProgress));
    }

    [Fact]
    public async Task Background_sweep_resolves_uncertain_payments_from_the_provider()
    {
        _provider.LoseNextResponse = true;
        await _service.ProcessAsync("key-1", Request, CancellationToken.None);
        var worker = new ReconciliationWorker(_store, _provider, NullLogger<ReconciliationWorker>.Instance);

        var resolved = await worker.SweepOnceAsync(CancellationToken.None);

        Assert.Equal(1, resolved);
        Assert.Empty(await _store.ListUncertainAsync(CancellationToken.None));
        var replay = (ProcessOutcome.Done)await _service.ProcessAsync("key-1", Request, CancellationToken.None);
        Assert.Equal(PaymentStatus.Succeeded, replay.Result.Status);
        Assert.Equal(1, _provider.ChargeCount);
    }

    [Fact]
    public async Task Declined_payment_is_stored_and_replayed_as_declined()
    {
        var big = Request with { AmountMinor = 5_000_000 };

        var first = (ProcessOutcome.Done)await _service.ProcessAsync("key-2", big, CancellationToken.None);
        var retry = (ProcessOutcome.Done)await _service.ProcessAsync("key-2", big, CancellationToken.None);

        Assert.Equal(PaymentStatus.Declined, first.Result.Status);
        Assert.Equal(first.Result, retry.Result);
    }
}
