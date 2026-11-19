namespace Payments.Api;

/// <summary>High-performance, structured log messages (source-generated).</summary>
internal static partial class Log
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Reconciled {Key}: provider had already processed it ({Status})")]
    public static partial void ReconciledExisting(ILogger logger, string key, Domain.PaymentStatus status);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Reconciled {Key}: provider has no record, charging now")]
    public static partial void ReconciledMissing(ILogger logger, string key);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Outcome of {Key} is unknown; marked for reconciliation")]
    public static partial void OutcomeUnknown(ILogger logger, Exception exception, string key);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "Reconciliation resolved {Count} uncertain payment(s)")]
    public static partial void SweepResolved(ILogger logger, int count);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Error, Message = "Reconciliation sweep failed")]
    public static partial void SweepFailed(ILogger logger, Exception exception);
}
