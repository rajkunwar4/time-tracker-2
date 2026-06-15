using TimeTrack.Core.Models;

namespace TimeTrack.Core.Storage;

/// <summary>
/// Durable local queue of interval records awaiting upload. The implementation
/// must be crash-safe: records survive process kills and power loss, and are
/// only removed once the server confirms receipt.
/// </summary>
public interface IOutboxRepository
{
    /// <summary>Create the schema if it does not yet exist.</summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>Append a record. Idempotent on <see cref="IntervalRecord.Id"/>.</summary>
    Task EnqueueAsync(IntervalRecord record, CancellationToken ct = default);

    /// <summary>Oldest-first batch of not-yet-sent records, capped at <paramref name="max"/>.</summary>
    Task<IReadOnlyList<IntervalRecord>> GetPendingBatchAsync(int max, CancellationToken ct = default);

    /// <summary>Mark records the server accepted as sent.</summary>
    Task MarkSentAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

    /// <summary>Record a failed attempt (increments attempt count, stores the error).</summary>
    Task MarkFailedAsync(IEnumerable<Guid> ids, string error, CancellationToken ct = default);

    /// <summary>How many records still need to be delivered.</summary>
    Task<int> CountPendingAsync(CancellationToken ct = default);
}
