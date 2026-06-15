using TimeTrack.Core.Models;

namespace TimeTrack.Core.Tracking;

/// <summary>
/// Accumulates per-second activity into fixed-length windows. Call <see cref="Tick"/>
/// once per second with whether the user was active; when a window fills it returns a
/// completed <see cref="IntervalRecord"/> ready to be enqueued in the outbox.
/// Idle seconds are simply not counted as active — they never inflate work time.
/// </summary>
public sealed class WindowedIntervalCollector
{
    private readonly string _email;
    private readonly int _windowSeconds;

    private DateTime _windowStartUtc;
    private int _elapsed;
    private int _activeSeconds;

    public WindowedIntervalCollector(string email, int windowSeconds = 60, DateTime? startUtc = null)
    {
        _email = email;
        _windowSeconds = Math.Max(1, windowSeconds);
        _windowStartUtc = startUtc ?? DateTime.UtcNow;
    }

    /// <summary>Seconds accumulated in the current (not-yet-emitted) window.</summary>
    public int PendingActiveSeconds => _activeSeconds;

    /// <summary>Call once per second. Returns a completed interval when the window fills, else null.</summary>
    public IntervalRecord? Tick(bool active)
    {
        _elapsed++;
        if (active) _activeSeconds++;

        if (_elapsed < _windowSeconds) return null;
        return CloseWindow();
    }

    /// <summary>Force-close a partial window (e.g. at logout). Returns null if nothing accumulated.</summary>
    public IntervalRecord? Flush()
    {
        if (_elapsed == 0) return null;
        return CloseWindow();
    }

    private IntervalRecord CloseWindow()
    {
        var record = new IntervalRecord
        {
            Email = _email,
            WindowStartUtc = _windowStartUtc,
            WindowEndUtc = DateTime.UtcNow,
            ActiveSeconds = _activeSeconds
        };

        _windowStartUtc = DateTime.UtcNow;
        _elapsed = 0;
        _activeSeconds = 0;
        return record;
    }
}
