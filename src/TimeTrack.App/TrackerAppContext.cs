using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TimeTrack.App.Forms;
using TimeTrack.Core.Api;
using TimeTrack.Core.Configuration;
using TimeTrack.Core.Security;
using TimeTrack.Core.Storage;

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

    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _syncItem;
    private Form? _current;
    private FrmMain? _main;

    public TrackerAppContext(AppSettings settings, IOutboxRepository outbox,
        TimeTrackApiClient api, ITokenStore tokenStore)
    {
        _settings = settings;
        _outbox = outbox;
        _api = api;
        _tokenStore = tokenStore;

        var menu = new ContextMenuStrip();
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
    }

    private void ShowLogin()
    {
        var login = new FrmLogin(_api, _tokenStore);
        login.LoginSucceeded += ShowMain;
        Swap(login);
        _main = null;
        _syncItem.Enabled = false;
        _tray.Text = "TimeTrack — signed out";
    }

    private void ShowMain(string email)
    {
        var main = new FrmMain(_settings, _outbox, _api, _tokenStore, email);
        main.LogoutRequested += ShowLogin;
        Swap(main);
        _main = main;
        _syncItem.Enabled = true;
        _tray.Text = "TimeTrack — " + email;
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
        if (_current == null) return;
        _current.Show();
        _current.WindowState = FormWindowState.Normal;
        _current.Activate();
    }

    private async Task SyncNowAsync()
    {
        if (_main != null) await _main.SyncNowAsync();
    }

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
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.Dispose(disposing);
    }
}
