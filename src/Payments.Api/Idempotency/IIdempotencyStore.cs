using Payments.Api.Domain;

namespace Payments.Api.Idempotency;

/// <summary>
/// Durable store for idempotency keys. <see cref="TryBeginAsync"/> must be atomic:
/// in a relational database this is an INSERT guarded by a unique constraint on the key
/// (see docs/postgres-schema.sql), never a SELECT followed by a separate INSERT.
/// </summary>
public interface IIdempotencyStore
{
    Task<BeginOutcome> TryBeginAsync(string key, string fingerprint, TimeSpan lease, CancellationToken ct);

    Task CompleteAsync(string key, PaymentResult result, CancellationToken ct);

    Task MarkUncertainAsync(string key, CancellationToken ct);

    Task<IReadOnlyList<IdempotencyRecord>> ListUncertainAsync(CancellationToken ct);
}
