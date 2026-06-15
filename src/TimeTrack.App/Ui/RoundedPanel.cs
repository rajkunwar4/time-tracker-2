using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TimeTrack.App.Ui;

/// <summary>A panel that paints a rounded fill + thin border (cards, fields, note boxes).</summary>
internal class RoundedPanel : Panel
{
    public int Radius { get; set; } = Theme.RadiusCard;
    public Color FillColor { get; set; } = Theme.Surface;
    public Color BorderColor { get; set; } = Theme.Border;
    public bool DrawBorder { get; set; } = true;

    public RoundedPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Draw.RoundedRect(r, Radius);

        using (var fill = new SolidBrush(FillColor))
            e.Graphics.FillPath(fill, path);

        if (DrawBorder)
        {
            using var pen = new Pen(BorderColor, 1f);
            e.Graphics.DrawPath(pen, path);
        }
    }
}
