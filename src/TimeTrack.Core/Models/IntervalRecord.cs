namespace TimeTrack.Core.Models;

/// <summary>Delivery state of a queued interval in the local outbox.</summary>
public enum OutboxStatus
{
    Pending = 0,
    Sending = 1,
    Sent = 2
}

/// <summary>
/// A single unit of tracked active time, queued locally until it can be
/// flushed to the cloud API during an internet window.
///
/// <para><b>Idempotency:</b> <see cref="Id"/> is a stable, client-generated GUID.
/// It is the dedupe key the server uses so that retries after a long offline
/// period never double-count time.</para>
/// </summary>
public sealed class IntervalRecord
{
    /// <summary>Stable client-generated idempotency key.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Email { get; init; } = string.Empty;

    public DateTime WindowStartUtc { get; init; }

    public DateTime WindowEndUtc { get; init; }

    /// <summary>Active (non-idle, non-break) seconds within the window.</summary>
    public int ActiveSeconds { get; init; }

    /// <summary>When the client recorded this interval (UTC).</summary>
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;

    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    /// <summary>Number of send attempts (drives backoff / poison detection).</summary>
    public int AttemptCount { get; set; }

    public DateTime? LastAttemptUtc { get; set; }

    public string? LastError { get; set; }
}
