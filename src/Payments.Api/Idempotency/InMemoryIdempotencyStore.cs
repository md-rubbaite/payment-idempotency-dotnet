using Payments.Api.Domain;

namespace Payments.Api.Idempotency;

/// <summary>Thread-safe in-memory store for demos and tests. Use a database-backed store in production.</summary>
public sealed class InMemoryIdempotencyStore(TimeProvider clock) : IIdempotencyStore
{
    private readonly Dictionary<string, IdempotencyRecord> _records = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public Task<BeginOutcome> TryBeginAsync(string key, string fingerprint, TimeSpan lease, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        lock (_gate)
        {
            if (!_records.TryGetValue(key, out var existing))
            {
                _records[key] = new IdempotencyRecord(key, fingerprint, IdempotencyState.InProgress, null, now, now + lease);
                return Task.FromResult<BeginOutcome>(new BeginOutcome.Acquired());
            }

            if (!string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return Task.FromResult<BeginOutcome>(new BeginOutcome.FingerprintMismatch());
            }

            BeginOutcome outcome = existing.State switch
            {
                IdempotencyState.Completed => new BeginOutcome.Replay(existing.Result!),
                IdempotencyState.Uncertain => new BeginOutcome.NeedsReconciliation(existing),
                // A crashed worker leaves an expired lease: treat it like an uncertain outcome, never as "free to charge again".
                IdempotencyState.InProgress when existing.LeaseExpiresAt <= now => new BeginOutcome.NeedsReconciliation(existing),
                _ => new BeginOutcome.Conflict(),
            };
            return Task.FromResult(outcome);
        }
    }

    public Task CompleteAsync(string key, PaymentResult result, CancellationToken ct)
    {
        lock (_gate)
        {
            var r = _records[key];
            _records[key] = r with { State = IdempotencyState.Completed, Result = result };
        }

        return Task.CompletedTask;
    }

    public Task MarkUncertainAsync(string key, CancellationToken ct)
    {
        lock (_gate)
        {
            var r = _records[key];
            _records[key] = r with { State = IdempotencyState.Uncertain };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<IdempotencyRecord>> ListUncertainAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            IReadOnlyList<IdempotencyRecord> list = _records.Values.Where(r => r.State == IdempotencyState.Uncertain).ToList();
            return Task.FromResult(list);
        }
    }
}
