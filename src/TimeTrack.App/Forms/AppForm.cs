using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TimeTrack.App.Ui;

namespace TimeTrack.App.Forms;

/// <summary>
/// Borderless base form with rounded corners, a thin border, and drag-to-move.
/// All windows in the app inherit the same chrome.
/// </summary>
internal class AppForm : Form
{
    protected int CornerRadius = Theme.RadiusWindow;

    public AppForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Surface;
        ForeColor = Theme.TextPrimary;
        Font = Theme.FontBody;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyRegion();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ApplyRegion();
    }

    private void ApplyRegion()
    {
        if (Width <= 0 || Height <= 0) return;
        using var path = Draw.RoundedRect(new Rectangle(0, 0, Width, Height), CornerRadius);
        Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Theme.Border, 1f);
        using var path = Draw.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius);
        e.Graphics.DrawPath(pen, path);
    }

    // ---- drag-to-move (borderless windows have no title bar) ----
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 0x2;

    /// <summary>Make a control act as a drag handle for the window.</summary>
    protected void EnableDrag(Control c)
    {
        c.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
        };
    }
}
