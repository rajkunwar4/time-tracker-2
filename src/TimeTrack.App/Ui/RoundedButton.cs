using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TimeTrack.App.Ui;

/// <summary>
/// Flat, rounded button honouring the design tokens. Supports a solid (filled)
/// style and an outline style.
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
        BackColor = Color.Transparent;
        ForeColor = Color.White;
        Font = Theme.FontButton;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // Skip the default background so the parent's surface shows through the corners.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Draw.RoundedRect(r, Radius);

        if (Outline)
        {
            using var pen = new Pen(OutlineColor, 1f);
            e.Graphics.DrawPath(pen, path);
        }
        else
        {
            using var fill = new SolidBrush(FillColor);
            e.Graphics.FillPath(fill, path);
        }

        TextRenderer.DrawText(
            e.Graphics, Text, Font, r, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}
