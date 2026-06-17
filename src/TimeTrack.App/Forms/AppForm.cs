using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TimeTrack.App.Ui;

namespace TimeTrack.App.Forms;

/// <summary>
/// Base form for the app's windows: a standard, resizable Windows window (title bar with
/// minimize / maximize / close) with the app's surface colour and DPI-aware scaling.
///
/// <para>Content is built as a single fixed-width panel and mounted **centered** via
/// <see cref="MountCentered"/>; the window's minimum size is locked to that content
/// (<see cref="LockMinimumToContent"/>) so it can grow but never clip.</para>
/// </summary>
internal class AppForm : Form
{
    public AppForm()
    {
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;   // scaling is applied explicitly via DpiScale
        BackColor = Theme.Surface;
        ForeColor = Theme.TextPrimary;
        Font = Theme.FontBody;
        DoubleBuffered = true;
        Text = "TimeTrack";
        ShowInTaskbar = true;
    }

    /// <summary>Scale a logical (96-DPI) pixel value to the current device DPI.</summary>
    protected static int Dpi(int logical) => DpiScale.Scale(logical);

    /// <summary>
    /// Mount a fixed-width content panel horizontally centered (and top-aligned) in the window,
    /// using a 3-column host grid (50% | content | 50%) so it stays centered as the window resizes.
    /// </summary>
    protected void MountCentered(Control content)
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            ColumnCount = 3,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        content.Anchor = AnchorStyles.Top;
        content.Margin = Padding.Empty;
        host.Controls.Add(content, 1, 0);
        Controls.Add(host);
    }

    /// <summary>
    /// Fit the window to the content (fixed design width, measured height), forbid shrinking
    /// below it, and center it. Call once in OnShown. The width is taken from the known design
    /// width rather than PreferredSize, which is unreliable for nested TableLayoutPanels.
    /// </summary>
    protected void LockMinimumToContent(Control content, int designWidth)
    {
        int w = Dpi(designWidth);
        int h = content.PreferredSize.Height;
        var chrome = Size - ClientSize;            // title bar + borders
        ClientSize = new Size(w, h);
        MinimumSize = new Size(w + chrome.Width, h + chrome.Height);

        // CenterScreen ran before this resize, so the grown window can end up off-screen;
        // force a correct center via SetWindowPos.
        CenterOnActiveScreen();
        Refresh();   // clean full repaint after the resize/move (clears any transient paint)
    }

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    /// <summary>Center the window on the monitor under the cursor, bypassing WinForms positioning quirks.</summary>
    protected void CenterOnActiveScreen()
    {
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        int x = wa.X + Math.Max(0, (wa.Width - Width) / 2);
        int y = wa.Y + Math.Max(0, (wa.Height - Height) / 2);
        SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    // ---- drag-to-move helper (kept for compatibility; harmless with a title bar) ----
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 0x2;

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
