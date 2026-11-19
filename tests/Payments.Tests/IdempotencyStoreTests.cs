using Payments.Api.Domain;
using Payments.Api.Idempotency;

namespace Payments.Tests;

public sealed class IdempotencyStoreTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Expired_lease_from_a_crashed_worker_requires_reconciliation_not_a_new_charge()
    {
        var clock = new ManualClock();
        var store = new InMemoryIdempotencyStore(clock);
        var fp = RequestFingerprint.Compute(new PaymentRequest("c", 1, "USD", "d"));

        Assert.IsType<BeginOutcome.Acquired>(await store.TryBeginAsync("k", fp, TimeSpan.FromSeconds(30), CancellationToken.None));
        Assert.IsType<BeginOutcome.Conflict>(await store.TryBeginAsync("k", fp, TimeSpan.FromSeconds(30), CancellationToken.None));

        clock.Now += TimeSpan.FromMinutes(1);

        Assert.IsType<BeginOutcome.NeedsReconciliation>(await store.TryBeginAsync("k", fp, TimeSpan.FromSeconds(30), CancellationToken.None));
    }

    [Theory]
    [InlineData("usd")]
    [InlineData("USD")]
    public void Fingerprint_ignores_currency_case(string currency)
    {
        var a = RequestFingerprint.Compute(new PaymentRequest("c", 100, currency, "d"));
        var b = RequestFingerprint.Compute(new PaymentRequest("c", 100, "USD", "d"));

        Assert.Equal(b, a);
    }
}
