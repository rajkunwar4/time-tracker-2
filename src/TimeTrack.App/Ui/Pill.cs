using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TimeTrack.App.Ui;

/// <summary>
/// Status pill (spec: radius 999, a coloured dot + a short label). Auto-sizes to its
/// text so it can sit centred in a layout cell (Anchor = None).
/// </summary>
internal sealed class Pill : Control
{
    private Color _fill = Theme.WorkPillBg;
    private Color _dot = Theme.WorkPrimary;
    private Color _label = Theme.WorkPillText;

    public Pill()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = Theme.FontPill;
        AutoSize = true;
    }

    public Color FillColor { get => _fill; set { _fill = value; Invalidate(); } }
    public Color DotColor { get => _dot; set { _dot = value; Invalidate(); } }
    public Color LabelColor { get => _label; set { _label = value; Invalidate(); } }

    private int Pad => DpiScale.Scale(16);
    private int DotSize => DpiScale.Scale(8);
    private int Gap => DpiScale.Scale(8);

    public override Size GetPreferredSize(Size proposedSize)
    {
        var textSize = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        int w = Pad + DotSize + Gap + textSize.Width + Pad + DpiScale.Scale(4); // small buffer so text never clips
        int h = textSize.Height + DpiScale.Scale(14);
        return new Size(w, h);
    }

    private void RecalcSize()
    {
        if (AutoSize) Size = GetPreferredSize(Size.Empty);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        RecalcSize();
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        RecalcSize();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Draw.RoundedRect(r, r.Height / 2))
        using (var b = new SolidBrush(_fill))
            g.FillPath(b, path);

        int dotY = (Height - DotSize) / 2;
        using (var d = new SolidBrush(_dot))
            g.FillEllipse(d, Pad, dotY, DotSize, DotSize);

        var textRect = new Rectangle(Pad + DotSize + Gap, 0, Width - (Pad + DotSize + Gap) - Pad, Height);
        TextRenderer.DrawText(g, Text, Font, textRect, _label,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}
