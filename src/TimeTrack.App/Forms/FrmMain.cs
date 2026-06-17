using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TimeTrack.App.Services;
using TimeTrack.App.Ui;
using TimeTrack.Core.Api;
using TimeTrack.Core.Configuration;
using TimeTrack.Core.Security;
using TimeTrack.Core.Storage;
using TimeTrack.Core.Sync;
using TimeTrack.Core.Tracking;

namespace TimeTrack.App.Forms;

/// <summary>
/// Screen 02/03 — the single home screen. Live work timer (idle excluded), a day
/// timeline, and three stat buckets. Real activity is sampled each second and queued
/// into the durable outbox; logging out flushes the queue to the API.
///
/// <para>Laid out with nested <see cref="TableLayoutPanel"/>s in logical units + font-based
/// auto-scaling — no absolute coordinates — so spacing and alignment stay consistent
/// across DPI settings.</para>
/// </summary>
internal sealed class FrmMain : AppForm
{
    private const int DesignWidth = 460;

    private readonly AppSettings _settings;
    private readonly IOutboxRepository _outbox;
    private readonly ITokenStore _tokenStore;
    private readonly OutboxSyncService _sync;
    private readonly WindowedIntervalCollector _collector;
    private readonly string _email;
    private readonly DateTime _loginTime = DateTime.Now;

    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    private int _workSeconds;
    private int _breakSeconds;
    private int _idleSeconds;
    private bool _onBreak;
    private bool _loggingOut;

    private readonly TableLayoutPanel _root;
    private readonly Pill _pill;
    private readonly Label _timer;
    private readonly Label _timerCaption;
    private readonly Label _tlNow;
    private readonly TimelineBar _timeline;
    private readonly StatCard _cardBreaks;
    private readonly StatCard _cardIdle;
    private readonly RoundedButton _btnBreak;
    private readonly RoundedButton _btnLogout;
    private readonly Label _offlineNote;

    /// <summary>Raised after the user logs out (and sync is attempted) — the app returns to login.</summary>
    public event Action? LogoutRequested;

    public FrmMain(AppSettings settings, IOutboxRepository outbox, TimeTrackApiClient api,
        ITokenStore tokenStore, string email)
    {
        _settings = settings;
        _outbox = outbox;
        _tokenStore = tokenStore;
        _sync = new OutboxSyncService(outbox, api);
        _email = email;
        _collector = new WindowedIntervalCollector(email, settings.Tracking.WindowSeconds);

        ClientSize = new Size(Dpi(DesignWidth), Dpi(470));

        _root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = Theme.Surface,
            Padding = new Padding(Dpi(20), Dpi(18), Dpi(20), Dpi(18))
        };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Dpi(DesignWidth - 40))); // 20px padding each side

        // ---- header: app name (left) + email (right) ----
        var header = BuildHeader();

        // ---- status pill (centred) ----
        _pill = new Pill { Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 0, Dpi(14)) };

        // ---- timer + caption ----
        _timer = new Label
        {
            Text = "00:00:00", Font = Theme.FontDisplayTimer, ForeColor = Theme.TextPrimary,
            AutoSize = false, Dock = DockStyle.Fill, Height = Dpi(56),
            TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, Dpi(2))
        };
        EnableDrag(_timer);
        _timerCaption = new Label
        {
            Text = "Total work time today", Font = Theme.FontCaption, ForeColor = Theme.TextSecondary,
            AutoSize = false, Dock = DockStyle.Fill, Height = Dpi(18),
            TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, Dpi(18))
        };

        // ---- timeline labels + bar + legend ----
        var tlLabels = BuildTimelineLabels(out _tlNow);
        _timeline = new TimelineBar
        {
            Dock = DockStyle.Fill, Height = Dpi(14),
            Margin = new Padding(0, 0, 0, Dpi(8))
        };
        var legend = BuildLegend();

        // ---- stat cards ----
        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = false, ColumnCount = 3, RowCount = 1,
            BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, Dpi(16)), Height = Dpi(64)
        };
        for (int i = 0; i < 3; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3f));
        var cardLogged = new StatCard("Logged in", _loginTime.ToString("h:mm tt")) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, Dpi(6), 0) };
        _cardBreaks = new StatCard("Breaks", "0:00 · 0") { Dock = DockStyle.Fill, Margin = new Padding(Dpi(3), 0, Dpi(3), 0) };
        _cardIdle = new StatCard("Idle", "0:00") { Dock = DockStyle.Fill, Margin = new Padding(Dpi(6), 0, 0, 0) };
        cards.Controls.Add(cardLogged, 0, 0);
        cards.Controls.Add(_cardBreaks, 1, 0);
        cards.Controls.Add(_cardIdle, 2, 0);

        // ---- buttons: break (1.6) + logout (1.0) ----
        int contentW = Dpi(DesignWidth - 40);
        int btnGap = Dpi(12);
        int btnH = Dpi(44);
        int breakW = (int)((contentW - btnGap) * 1.6 / 2.6);   // break : logout ≈ 1.6 : 1
        int logoutW = contentW - btnGap - breakW;
        // Explicit bounds in a plain panel — deterministic, no layout-engine overlap.
        var buttons = new Panel
        {
            Width = contentW, Height = btnH, BackColor = Theme.Surface,
            Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 0, Dpi(12))
        };
        _btnBreak = new RoundedButton
        {
            Text = "Take a break", FillColor = Theme.BreakSurface, ForeColor = Theme.BreakText,
            Radius = Theme.RadiusCard, Location = new Point(0, 0), Size = new Size(breakW, btnH)
        };
        _btnBreak.Click += (_, _) => ToggleBreak();
        _btnLogout = new RoundedButton
        {
            Text = "↻  Log out", Outline = true, OutlineColor = Theme.Border, ForeColor = Theme.LogoutText,
            Radius = Theme.RadiusCard, Location = new Point(breakW + btnGap, 0), Size = new Size(logoutW, btnH)
        };
        _btnLogout.Click += async (_, _) => await LogoutAndSyncAsync();
        buttons.Controls.Add(_btnBreak);
        buttons.Controls.Add(_btnLogout);

        // ---- offline note ----
        _offlineNote = new Label
        {
            Text = "Offline · your data syncs automatically when you log out",
            Font = Theme.FontCaption, ForeColor = Theme.TextMuted, AutoSize = false,
            Dock = DockStyle.Fill, Height = Dpi(18),
            TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent,
            Margin = Padding.Empty
        };

        foreach (var c in new Control[] { header, _pill, _timer, _timerCaption, tlLabels,
                     _timeline, legend, cards, buttons, _offlineNote })
            _root.Controls.Add(c);
        // Pin the card to the design width; height auto-sizes to content.
        _root.MinimumSize = new Size(Dpi(DesignWidth), 0);
        _root.MaximumSize = new Size(Dpi(DesignWidth), 0);
        MountCentered(_root);

        UpdateStatusPill();
        UpdateTimerDisplay();

        _tick.Tick += OnTick;
        _tick.Start();
        FormClosed += (_, _) => _tick.Dispose();

        _ = UpdatePendingAsync();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        LockMinimumToContent(_root, DesignWidth);
    }

    // ---- header / timeline-labels / legend builders ----
    private TableLayoutPanel BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1,
            BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, Dpi(16))
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var left = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false, BackColor = Color.Transparent,
            Anchor = AnchorStyles.Left, Margin = Padding.Empty
        };
        var clock = new Panel { Width = Dpi(18), Height = Dpi(18), BackColor = Color.Transparent, Margin = new Padding(0, Dpi(1), Dpi(8), 0) };
        clock.Paint += (_, e) => DrawClock(e.Graphics, new Rectangle(0, 0, Dpi(18) - 1, Dpi(18) - 1), Theme.WorkPrimary, Dpi(2));
        var appName = new Label
        {
            Text = "TimeTrack", Font = Theme.FontAppName, ForeColor = Theme.TextPrimary,
            AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, Dpi(1), 0, 0)
        };
        EnableDrag(appName);
        left.Controls.Add(clock);
        left.Controls.Add(appName);

        var email = new Label
        {
            Text = _email, Font = Theme.FontBody, ForeColor = Theme.TextSecondary,
            AutoSize = true, BackColor = Color.Transparent, Anchor = AnchorStyles.Right,
            Margin = new Padding(0, Dpi(2), 0, 0)
        };

        header.Controls.Add(left, 0, 0);
        header.Controls.Add(email, 1, 0);
        return header;
    }

    private TableLayoutPanel BuildTimelineLabels(out Label nowLabel)
    {
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1,
            BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, Dpi(6))
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));

        var start = new Label { Text = _loginTime.ToString("h:mm tt"), Font = Theme.FontCaption, ForeColor = Theme.TextSecondary, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent };
        var mid = new Label { Text = "12 PM", Font = Theme.FontCaption, ForeColor = Theme.TextSecondary, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent };
        nowLabel = new Label { Text = "Now · " + DateTime.Now.ToString("h:mm tt"), Font = Theme.FontCaption, ForeColor = Theme.TextSecondary, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, BackColor = Color.Transparent };

        t.Controls.Add(start, 0, 0);
        t.Controls.Add(mid, 1, 0);
        t.Controls.Add(nowLabel, 2, 0);
        return t;
    }

    private FlowLayoutPanel BuildLegend()
    {
        var p = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false, BackColor = Color.Transparent,
            Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 0, Dpi(16))
        };
        p.Controls.Add(LegendItem("Work", Theme.WorkPrimary));
        p.Controls.Add(LegendItem("Break", Theme.BreakSegment));
        p.Controls.Add(LegendItem("Idle", Theme.IdleSegment));
        return p;
    }

    private Label LegendItem(string text, Color color)
    {
        var item = new Label
        {
            Text = "    " + text, Font = Theme.FontCaption, ForeColor = Theme.TextSecondary,
            AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 0, Dpi(14), 0)
        };
        item.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var b = new SolidBrush(color);
            e.Graphics.FillEllipse(b, 0, item.Height / 2 - Dpi(4), Dpi(8), Dpi(8));
        };
        return item;
    }

    private static void DrawClock(Graphics g, Rectangle r, Color color, int penW)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, penW) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        int inset = penW;
        var c = new Rectangle(r.X + inset, r.Y + inset, r.Width - inset * 2, r.Height - inset * 2);
        g.DrawEllipse(pen, c);
        int cx = c.X + c.Width / 2, cy = c.Y + c.Height / 2;
        g.DrawLine(pen, cx, cy, cx, cy - c.Height / 4);            // hour hand
        g.DrawLine(pen, cx, cy, cx + c.Width / 4, cy);             // minute hand
    }

    // ---- tracking loop (unchanged behaviour) ----
    private async void OnTick(object? sender, EventArgs e)
    {
        bool idle = Win32Idle.GetIdleTime().TotalSeconds >= _settings.Tracking.IdleThresholdSeconds;

        bool active;
        if (_onBreak) { _breakSeconds++; active = false; }
        else if (idle) { _idleSeconds++; active = false; }
        else { _workSeconds++; active = true; }

        var record = _collector.Tick(active);
        if (record is { ActiveSeconds: > 0 })
        {
            await _outbox.EnqueueAsync(record);
            await UpdatePendingAsync();
        }

        UpdateTimerDisplay();
        UpdateStatusPill();
        UpdateBreaksValue();
        UpdateIdleValue();
        _tlNow.Text = "Now · " + DateTime.Now.ToString("h:mm tt");
        _timeline.SetSegments(_workSeconds, _breakSeconds, _idleSeconds);
    }

    private async Task LogoutAndSyncAsync()
    {
        if (_loggingOut) return;
        _loggingOut = true;
        _tick.Stop();
        _btnLogout.Enabled = false;
        _btnLogout.Text = "Syncing…";

        try
        {
            var tail = _collector.Flush();
            if (tail is { ActiveSeconds: > 0 })
                await _outbox.EnqueueAsync(tail);

            var token = _tokenStore.Load()?.Token;
            if (string.IsNullOrEmpty(token))
            {
                MessageBox.Show("No saved session token — please sign in again.", "TimeTrack",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                var result = await _sync.FlushAsync(token);
                var pending = await _outbox.CountPendingAsync();
                if (result.Success)
                    MessageBox.Show($"Synced {result.AcceptedCount} interval(s). {pending} still queued.",
                        "Sync complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show($"Couldn't sync ({result.Error}). Your data is safe locally and will retry next time. {pending} queued.",
                        "Sync failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            LogoutRequested?.Invoke();
        }
    }

    /// <summary>Flush the outbox to the API without logging out (tray "Sync now").</summary>
    public async Task SyncNowAsync()
    {
        if (_loggingOut) return;
        var token = _tokenStore.Load()?.Token;
        if (string.IsNullOrEmpty(token)) return;
        try { await _sync.FlushAsync(token); }
        catch { /* anything unsent stays queued for next time */ }
        await UpdatePendingAsync();
    }

    private void ToggleBreak()
    {
        _onBreak = !_onBreak;

        if (_onBreak)
        {
            _btnBreak.Text = "▶  Resume work";
            _btnBreak.FillColor = Theme.WorkPrimary;
            _btnBreak.ForeColor = Color.White;
            _timer.ForeColor = Theme.TextMuted;
            _timerCaption.Text = "Total work time · paused";
            _cardBreaks.FillColor = Theme.BreakSurface;
            _cardBreaks.ValueColor = Theme.BreakText;
        }
        else
        {
            _btnBreak.Text = "Take a break";
            _btnBreak.FillColor = Theme.BreakSurface;
            _btnBreak.ForeColor = Theme.BreakText;
            _timer.ForeColor = Theme.TextPrimary;
            _timerCaption.Text = "Total work time today";
            _cardBreaks.FillColor = Theme.CardFill;
            _cardBreaks.ValueColor = Theme.TextPrimary;
        }

        _cardBreaks.Invalidate();
        UpdateBreaksValue();
        UpdateStatusPill();
        _btnBreak.Invalidate();
    }

    private async Task UpdatePendingAsync()
    {
        try
        {
            int pending = await _outbox.CountPendingAsync();
            _offlineNote.Text = pending == 0
                ? "Offline · your data syncs automatically when you log out"
                : $"Offline · {pending} interval(s) queued · syncs at logout";
        }
        catch { /* non-fatal UI nicety */ }
    }

    private void UpdateTimerDisplay()
    {
        var ts = TimeSpan.FromSeconds(_workSeconds);
        _timer.Text = $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
    }

    private void UpdateBreaksValue()
    {
        var ts = TimeSpan.FromSeconds(_breakSeconds);
        _cardBreaks.Value = $"{(int)ts.TotalHours}:{ts.Minutes:00} · {(_breakSeconds > 0 ? 1 : 0)}";
    }

    private void UpdateIdleValue()
    {
        var ts = TimeSpan.FromSeconds(_idleSeconds);
        _cardIdle.Value = $"{(int)ts.TotalHours}:{ts.Minutes:00}";
    }

    private void UpdateStatusPill()
    {
        if (_onBreak)
        {
            _pill.Text = $"On break · {_breakSeconds / 60}:{_breakSeconds % 60:00}";
            _pill.FillColor = Theme.BreakSurface;
            _pill.DotColor = Theme.BreakSegment;
            _pill.LabelColor = Theme.BreakText;
        }
        else
        {
            _pill.Text = "Working";
            _pill.FillColor = Theme.WorkPillBg;
            _pill.DotColor = Theme.WorkPrimary;
            _pill.LabelColor = Theme.WorkPillText;
        }
    }
}
