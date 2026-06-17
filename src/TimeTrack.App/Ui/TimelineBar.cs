using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TimeTrack.App.Ui;

/// <summary>
/// The day timeline (spec 02): a rounded track filled left-to-right with proportional
/// Work / Break / Idle segments. Set the three second-counts and call Invalidate.
/// </summary>
internal sealed class TimelineBar : Control
{
    private int _work, _break, _idle;

    public TimelineBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public void SetSegments(int workSeconds, int breakSeconds, int idleSeconds)
    {
        _work = workSeconds;
        _break = breakSeconds;
        _idle = idleSeconds;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int w = Width, h = Height;
        using var path = Draw.RoundedRect(new Rectangle(0, 0, w, h), DpiScale.Scale(Theme.RadiusBar));

        using (var track = new SolidBrush(Theme.CardFill))
            g.FillPath(track, path);

        int total = Math.Max(1, _work + _break + _idle);
        int workW = (int)(w * (_work / (double)total));
        int breakW = (int)(w * (_break / (double)total));
        int idleW = (_work + _break + _idle) == 0 ? 0 : w - workW - breakW;

        var prevClip = g.Clip;
        g.SetClip(path);

        int x = 0;
        Fill(g, x, h, workW, Theme.WorkPrimary); x += workW;
        Fill(g, x, h, breakW, Theme.BreakSegment); x += breakW;
        Fill(g, x, h, idleW, Theme.IdleSegment);

        g.Clip = prevClip;
    }

    private static void Fill(Graphics g, int x, int h, int w, Color color)
    {
        if (w <= 0) return;
        using var b = new SolidBrush(color);
        g.FillRectangle(b, x, 0, w, h);
    }
}
