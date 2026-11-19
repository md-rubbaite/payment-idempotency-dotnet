using Payments.Api.Domain;

namespace Payments.Api.Idempotency;

public enum IdempotencyState
{
    InProgress,
    Completed,

    /// <summary>The outcome is uncertain (for example, a provider timeout). Reconcile before answering.</summary>
    Uncertain,
}

public sealed record IdempotencyRecord(
    string Key,
    string RequestFingerprint,
    IdempotencyState State,
    PaymentResult? Result,
    DateTimeOffset CreatedAt,
    DateTimeOffset LeaseExpiresAt);

/// <summary>Outcome of trying to claim an idempotency key.</summary>
public abstract record BeginOutcome
{
    private BeginOutcome() { }

    /// <summary>The caller owns the key and must perform the operation.</summary>
    public sealed record Acquired : BeginOutcome;

    /// <summary>The operation already finished; return the stored result unchanged.</summary>
    public sealed record Replay(PaymentResult Result) : BeginOutcome;

    /// <summary>Another request with the same key is still running.</summary>
    public sealed record Conflict : BeginOutcome;

    /// <summary>The key was reused with a different request body.</summary>
    public sealed record FingerprintMismatch : BeginOutcome;

    /// <summary>A previous attempt ended with an uncertain outcome; reconcile it.</summary>
    public sealed record NeedsReconciliation(IdempotencyRecord Record) : BeginOutcome;
}
