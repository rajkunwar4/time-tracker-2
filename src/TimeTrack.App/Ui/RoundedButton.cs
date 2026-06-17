using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TimeTrack.App.Ui;

/// <summary>
/// Flat, rounded button honouring the design tokens. Supports a solid (filled) style and
/// an outline style. Fully owner-drawn and <b>opaque</b> (UserPaint + clear-to-surface each
/// paint) so the outline style never composites stale form content through its interior.
/// </summary>
internal class RoundedButton : Button
{
    public int Radius { get; set; } = Theme.RadiusBar;
    public Color FillColor { get; set; } = Theme.WorkPrimary;
    public Color OutlineColor { get; set; } = Theme.Border;
    public bool Outline { get; set; }

    public RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseOverBackColor = Color.Transparent;
        FlatAppearance.MouseDownBackColor = Color.Transparent;
        ForeColor = Color.White;
        Font = Theme.FontButton;
        Cursor = Cursors.Hand;
        // Own all painting, double-buffered, no transparent compositing.
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    /// <summary>The opaque surface the button sits on (its parent's colour, or the app surface).</summary>
    private Color Backdrop =>
        (Parent != null && Parent.BackColor != Color.Transparent) ? Parent.BackColor : Theme.Surface;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Backdrop);                 // opaque clear — no leftover/composited pixels
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Draw.RoundedRect(r, DpiScale.Scale(Radius));

        if (Outline)
        {
            using (var fill = new SolidBrush(Backdrop))   // interior = surface colour
                g.FillPath(fill, path);
            using var pen = new Pen(OutlineColor, 1f);
            g.DrawPath(pen, path);
        }
        else
        {
            using var fill = new SolidBrush(FillColor);
            g.FillPath(fill, path);
        }

        TextRenderer.DrawText(
            g, Text, Font, r, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}
