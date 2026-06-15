using TimeTrack.Core.Api;
using TimeTrack.Core.Storage;

namespace TimeTrack.Core.Sync;

public sealed class SyncResult
{
    public bool Success { get; init; }
    public int AcceptedCount { get; init; }
    public string? Error { get; init; }

    public static SyncResult Succeeded(int accepted) => new() { Success = true, AcceptedCount = accepted };
    public static SyncResult Failed(int accepted, string? error) =>
        new() { Success = false, AcceptedCount = accepted, Error = error };
}

/// <summary>
/// Drains the durable outbox to the API in batches: pull pending → POST → mark the
/// server-accepted ids as sent, mark the rest as failed (so they retry later). Stops
/// on the first transport/auth failure, leaving everything queued for the next window.
/// </summary>
public sealed class OutboxSyncService
{
    private readonly IOutboxRepository _outbox;
    private readonly TimeTrackApiClient _api;

    public OutboxSyncService(IOutboxRepository outbox, TimeTrackApiClient api)
    {
        _outbox = outbox;
        _api = api;
    }

    public async Task<SyncResult> FlushAsync(string token, int batchSize = 200, CancellationToken ct = default)
    {
        int totalAccepted = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = await _outbox.GetPendingBatchAsync(batchSize, ct).ConfigureAwait(false);
            if (batch.Count == 0) break;

            var result = await _api.PostIntervalsAsync(token, batch, ct).ConfigureAwait(false);

            if (!result.Success)
            {
                // Transport/auth/server error → leave queued, bump attempt counts, stop.
                await _outbox.MarkFailedAsync(batch.Select(b => b.Id), result.Error ?? "ingest failed", ct)
                    .ConfigureAwait(false);
                return SyncResult.Failed(totalAccepted, result.Error);
            }

            var acceptedSet = result.AcceptedIds.ToHashSet();
            await _outbox.MarkSentAsync(result.AcceptedIds, ct).ConfigureAwait(false);
            totalAccepted += result.AcceptedIds.Count;

            // Anything the server did NOT accept (e.g. validation-rejected) → mark failed so
            // it isn't re-fetched forever in this loop.
            var notAccepted = batch.Where(b => !acceptedSet.Contains(b.Id)).Select(b => b.Id).ToList();
            if (notAccepted.Count > 0)
                await _outbox.MarkFailedAsync(notAccepted, "not accepted by server", ct).ConfigureAwait(false);

            // No full page, or no progress → done.
            if (batch.Count < batchSize || result.AcceptedIds.Count == 0) break;
        }

        return SyncResult.Succeeded(totalAccepted);
    }
}
