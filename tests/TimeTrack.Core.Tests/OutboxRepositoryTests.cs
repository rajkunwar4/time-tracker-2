using TimeTrack.Core.Models;
using TimeTrack.Core.Storage;
using Xunit;

namespace TimeTrack.Core.Tests;

public sealed class OutboxRepositoryTests : IAsyncLifetime
{
    private string _dbPath = "";
    private SqliteOutboxRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"tt_test_{Guid.NewGuid():N}.db");
        _repo = new SqliteOutboxRepository(_dbPath);
        await _repo.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        foreach (var p in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            try { if (File.Exists(p)) File.Delete(p); } catch { /* best effort */ }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Enqueue_then_pending_returns_the_record()
    {
        var r = NewRecord(60);
        await _repo.EnqueueAsync(r);

        var pending = await _repo.GetPendingBatchAsync(10);

        Assert.Single(pending);
        Assert.Equal(r.Id, pending[0].Id);
        Assert.Equal(60, pending[0].ActiveSeconds);
        Assert.Equal(1, await _repo.CountPendingAsync());
    }

    [Fact]
    public async Task Enqueue_is_idempotent_on_id()
    {
        var r = NewRecord(30);
        await _repo.EnqueueAsync(r);
        await _repo.EnqueueAsync(r); // same GUID again

        Assert.Equal(1, await _repo.CountPendingAsync());
    }

    [Fact]
    public async Task MarkSent_removes_from_pending()
    {
        var a = NewRecord(10);
        var b = NewRecord(20);
        await _repo.EnqueueAsync(a);
        await _repo.EnqueueAsync(b);

        await _repo.MarkSentAsync(new[] { a.Id });

        var pending = await _repo.GetPendingBatchAsync(10);
        Assert.Single(pending);
        Assert.Equal(b.Id, pending[0].Id);
        Assert.Equal(1, await _repo.CountPendingAsync());
    }

    [Fact]
    public async Task MarkFailed_keeps_pending_and_bumps_attempt()
    {
        var a = NewRecord(10);
        await _repo.EnqueueAsync(a);

        await _repo.MarkFailedAsync(new[] { a.Id }, "boom");

        var pending = await _repo.GetPendingBatchAsync(10);
        Assert.Single(pending);
        Assert.Equal(1, pending[0].AttemptCount);
        Assert.Equal("boom", pending[0].LastError);
    }

    private static IntervalRecord NewRecord(int seconds) => new()
    {
        Email = "test@example.com",
        WindowStartUtc = DateTime.UtcNow.AddMinutes(-1),
        WindowEndUtc = DateTime.UtcNow,
        ActiveSeconds = seconds
    };
}
