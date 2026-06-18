using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TimeTrack.App.Forms;
using TimeTrack.Core.Api;
using TimeTrack.Core.Configuration;
using TimeTrack.Core.Security;
using TimeTrack.Core.Storage;
using TimeTrack.Core.Sync;

namespace TimeTrack.App;

/// <summary>
/// Owns the app lifecycle and the system-tray presence. The app is always-on: it shows
/// Login, swaps to Main on sign-in, returns to Login on logout, and **minimizes to the
/// tray** instead of exiting when a window is closed. There is intentionally **no Exit**
/// in the tray menu — only Open and Sync now.
/// </summary>
internal sealed class TrackerAppContext : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly IOutboxRepository _outbox;
    private readonly TimeTrackApiClient _api;
    private readonly ITokenStore _tokenStore;
    private readonly IInternetWindow _internetWindow;

    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _syncItem;
    private readonly EventWaitHandle _showWindowSignal;
    private readonly Thread _signalThread;
    private volatile bool _stopping;
    private Form? _current;
    private FrmMain? _main;

    public TrackerAppContext(AppSettings settings, IOutboxRepository outbox,
        TimeTrackApiClient api, ITokenStore tokenStore, IInternetWindow internetWindow,
        EventWaitHandle showWindowSignal)
    {
        _settings = settings;
        _outbox = outbox;
        _api = api;
        _tokenStore = tokenStore;
        _internetWindow = internetWindow;
        _showWindowSignal = showWindowSignal;
        _signalThread = new Thread(SignalLoop) { IsBackground = true, Name = "TimeTrack-ShowSignal" };

        var menu = new ContextMenuStrip();
        // Non-clickable header so the running build's version is always discoverable.
        menu.Items.Add(new ToolStripMenuItem($"{AppInfo.Name} {AppInfo.VersionLabel}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open", null, (_, _) => RestoreCurrent()));
        _syncItem = new ToolStripMenuItem("Sync now", null, async (_, _) => await SyncNowAsync()) { Enabled = false };
        menu.Items.Add(_syncItem);
        // No "Exit" — the tracker stays running (per decision).

        _tray = new NotifyIcon
        {
            Icon = BuildTrayIcon(),
            Text = "TimeTrack",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => RestoreCurrent();

        ShowLogin();

        // Listen for "show yourself" pokes from any second launch of the exe.
        _signalThread.Start();
    }

    private void ShowLogin()
    {
        var login = new FrmLogin(_api, _tokenStore);
        login.LoginSucceeded += ShowMain;
        Swap(login);
        _main = null;
        _syncItem.Enabled = false;
        _tray.Text = $"{AppInfo.Name} {AppInfo.VersionLabel} — signed out";
    }

    private void ShowMain(string email)
    {
        var main = new FrmMain(_settings, _outbox, _api, _tokenStore, _internetWindow, email);
        main.LogoutRequested += ShowLogin;
        Swap(main);
        _main = main;
        _syncItem.Enabled = true;
        // NotifyIcon.Text caps at 63 chars — keep version + email within it.
        _tray.Text = Truncate($"{AppInfo.Name} {AppInfo.VersionLabel} — {email}", 63);
    }

    /// <summary>Show the next window and tear down the previous one (deferred, off the event stack).</summary>
    private void Swap(Form next)
    {
        var prev = _current;
        _current = next;
        next.FormClosing += HideToTrayOnUserClose;
        next.Show();
        next.Activate();

        if (prev != null)
        {
            prev.FormClosing -= HideToTrayOnUserClose;
            prev.BeginInvoke(new Action(() =>
            {
                prev.Close();
                prev.Dispose();
            }));
        }
    }

    /// <summary>Closing a window minimizes to the tray instead of exiting the app.</summary>
    private void HideToTrayOnUserClose(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason != CloseReason.UserClosing) return;
        e.Cancel = true;
        ((Form)sender!).Hide();
        _tray.ShowBalloonTip(1500, "TimeTrack", "Still tracking in the background.", ToolTipIcon.Info);
    }

    private void RestoreCurrent()
    {
        var form = _current;
        if (form == null || form.IsDisposed) return;

        if (!form.Visible) form.Show();
        if (form.WindowState == FormWindowState.Minimized)
            form.WindowState = FormWindowState.Normal;

        // Windows blocks a background process from stealing focus, so Activate() alone often
        // just flashes the taskbar button. Toggling TopMost and then SetForegroundWindow
        // reliably raises the window to the front.
        form.TopMost = true;
        form.TopMost = false;
        form.Activate();
        SetForegroundWindow(form.Handle);
    }

    /// <summary>
    /// Background loop: a second launch of the app sets <see cref="_showWindowSignal"/>
    /// (instead of opening a new window) to ask this live instance to surface itself.
    /// </summary>
    private void SignalLoop()
    {
        while (!_stopping)
        {
            try { _showWindowSignal.WaitOne(); }
            catch (ObjectDisposedException) { return; }
            if (_stopping) return;

            var form = _current;
            if (form is { IsDisposed: false, IsHandleCreated: true })
            {
                // Marshal back to the UI thread — RestoreCurrent touches WinForms state.
                try { form.BeginInvoke(new Action(RestoreCurrent)); }
                catch (InvalidOperationException) { /* form torn down mid-swap; ignore */ }
            }
        }
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private async Task SyncNowAsync()
    {
        if (_main != null) await _main.SyncNowAsync();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    private static Icon BuildTrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(Theme.WorkPrimary);
            g.FillEllipse(bg, 1, 1, 30, 30);
            using var pen = new Pen(Color.White, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, 16, 16, 16, 8);   // hour hand
            g.DrawLine(pen, 16, 16, 22, 17);  // minute hand
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stopping = true;
            try { _showWindowSignal.Set(); } catch { /* primary owns disposal in Program */ }
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.Dispose(disposing);
    }
}
