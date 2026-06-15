using TimeTrack.Core.Tracking;
using Xunit;

namespace TimeTrack.Core.Tests;

public sealed class WindowedIntervalCollectorTests
{
    [Fact]
    public void Emits_record_after_window_fills_with_active_count()
    {
        var c = new WindowedIntervalCollector("e@e.com", windowSeconds: 5);

        TimeTrack.Core.Models.IntervalRecord? rec = null;
        for (int i = 0; i < 5; i++)
            rec = c.Tick(active: i % 2 == 0); // active on ticks 0,2,4 → 3 active seconds

        Assert.NotNull(rec);
        Assert.Equal(3, rec!.ActiveSeconds);
        Assert.Equal("e@e.com", rec.Email);
    }

    [Fact]
    public void Does_not_emit_before_window_fills()
    {
        var c = new WindowedIntervalCollector("e@e.com", windowSeconds: 5);
        for (int i = 0; i < 4; i++)
            Assert.Null(c.Tick(true));
    }

    [Fact]
    public void Flush_returns_partial_window_then_nothing()
    {
        var c = new WindowedIntervalCollector("e@e.com", windowSeconds: 60);
        c.Tick(true);
        c.Tick(true);

        var rec = c.Flush();
        Assert.NotNull(rec);
        Assert.Equal(2, rec!.ActiveSeconds);

        Assert.Null(c.Flush()); // window reset, nothing accumulated
    }

    [Fact]
    public void Idle_seconds_are_not_counted_as_active()
    {
        var c = new WindowedIntervalCollector("e@e.com", windowSeconds: 3);
        c.Tick(true);
        c.Tick(false); // idle
        var rec = c.Tick(false); // idle → window fills at 3 ticks, only 1 active

        Assert.NotNull(rec);
        Assert.Equal(1, rec!.ActiveSeconds);
    }
}
