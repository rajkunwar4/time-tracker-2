using System.Drawing;
using System.Windows.Forms;

namespace TimeTrack.App.Ui;

/// <summary>
/// A stat bucket (spec 02): a rounded card with a caption on top and a value below,
/// vertically centred. Used for "Logged in", "Breaks", "Idle".
/// </summary>
internal sealed class StatCard : RoundedPanel
{
    private readonly Label _caption;
    private readonly Label _value;

    public StatCard(string caption, string value)
    {
        Radius = Theme.RadiusCard;
        FillColor = Theme.CardFill;
        DrawBorder = false;
        Padding = new Padding(DpiScale.Scale(6));

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

        _caption = new Label
        {
            Text = caption,
            Font = Theme.FontCaption,
            ForeColor = Theme.TextSecondary,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomCenter,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        _value = new Label
        {
            Text = value,
            Font = Theme.FontValue,
            ForeColor = Theme.TextPrimary,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopCenter,
            BackColor = Color.Transparent,
            Margin = new Padding(0, DpiScale.Scale(2), 0, 0)
        };

        grid.Controls.Add(_caption, 0, 0);
        grid.Controls.Add(_value, 0, 1);
        Controls.Add(grid);
    }

    public string Value { get => _value.Text; set => _value.Text = value; }
    public Color ValueColor { get => _value.ForeColor; set => _value.ForeColor = value; }
}
