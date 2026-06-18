using System.Drawing;
using System.Windows.Forms;

namespace TimeTrack.App.Ui;

/// <summary>
/// A stat bucket (spec 02): a rounded card with a caption on top and a value below,
/// vertically centred. Used for "Logged in", "Breaks", "Idle".
///
/// <para><b>Cross-device sizing.</b> The card height is <i>defined by the actual measured
/// font line-heights</i> (<see cref="MeasureHeight"/>) plus padding and headroom — never a
/// hard-coded pixel count — so it adapts to whatever the client's machine renders (DPI,
/// font substitution, locale). Caption and value auto-size and are centred between spacer
/// rows, so they always get their full line-height and stay clear of the rounded corners;
/// the value can never be clipped. The card also enforces this height as its
/// <see cref="Control.MinimumSize"/>, so no parent layout can squeeze it and cut off the
/// bottom rounding.</para>
/// </summary>
internal sealed class StatCard : RoundedPanel
{
    private const int Pad = 6;          // inner padding, all sides (logical px)
    private const int CaptionGap = 2;   // gap between caption and value (logical px)
    private const int Headroom = 10;    // breathing room so text stays clear of the rounded edges

    private readonly Label _caption;
    private readonly Label _value;

    public StatCard(string caption, string value)
    {
        Radius = Theme.RadiusCard;
        FillColor = Theme.CardFill;
        DrawBorder = false;
        Padding = new Padding(DpiScale.Scale(Pad));

        // Lock in a height that fits caption + value at this machine's real metrics. As a
        // minimum (not a fixed size) the card can still grow with its cell, but can never be
        // shrunk below what the content needs — so the bottom rounded corners always render.
        MinimumSize = new Size(0, MeasureHeight());

        // 4-row grid: a percentage spacer above and below two auto-sized content rows.
        // The auto-sized rows always claim their full preferred height first; the spacers
        // absorb any leftover space, vertically centring the content without clipping it.
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f)); // top spacer
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // caption
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // value
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f)); // bottom spacer

        _caption = new Label
        {
            Text = caption,
            Font = Theme.FontCaption,
            ForeColor = Theme.TextSecondary,
            AutoSize = true,
            Anchor = AnchorStyles.None,                 // centre within the cell
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        _value = new Label
        {
            Text = value,
            Font = Theme.FontValue,
            ForeColor = Theme.TextPrimary,
            AutoSize = true,
            Anchor = AnchorStyles.None,                 // centre within the cell
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Margin = new Padding(0, DpiScale.Scale(CaptionGap), 0, 0)
        };

        grid.Controls.Add(_caption, 0, 1);
        grid.Controls.Add(_value, 0, 2);
        Controls.Add(grid);
    }

    /// <summary>
    /// Height a stat card needs to show caption + value (with padding and headroom) without
    /// clipping at the current DPI / font metrics. Driven by the actual measured font
    /// line-heights, so it adapts to whatever the client's machine renders rather than
    /// assuming the dev machine's. Callers size the cards row to this.
    /// </summary>
    public static int MeasureHeight() =>
        DpiScale.Scale(Pad) * 2
        + Theme.FontCaption.Height
        + DpiScale.Scale(CaptionGap)
        + Theme.FontValue.Height
        + DpiScale.Scale(Headroom);

    public string Value { get => _value.Text; set => _value.Text = value; }
    public Color ValueColor { get => _value.ForeColor; set => _value.ForeColor = value; }
}
