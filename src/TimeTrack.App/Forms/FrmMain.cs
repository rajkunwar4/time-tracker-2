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
/// </summary>
internal sealed class FrmMain : AppForm
{
    private const int Pad = 20;

    private readonly AppSettings _settings;
    private readonly IOutboxRepository _outbox;
    private readonly ITokenStore _tokenStore;
    private readonly OutboxSyncService _sync;
    private readonly WindowedIntervalCollector _collector;
    private readonly string _email;

    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    private int _workSeconds;
    private int _breakSeconds;
    private int _idleSeconds;
    private bool _onBreak;
    private bool _loggingOut;

    private readonly Panel _statusPill;
    private readonly Label _timer;
    private readonly Label _timerCaption;
    private readonly Panel _timeline;
    private readonly RoundedPanel _cardBreaks;
    private readonly Label _breaksValue;
    private readonly RoundedButton _btnBreak;
    private readonly RoundedButton _btnLogout;
    private readonly Label _offlineNote;

    public FrmMain(AppSettings settings, IOutboxRepository outbox, TimeTrackApiClient api,
        ITokenStore tokenStore, string email)
    {
        _settings = settings;
        _outbox = outbox;
        _tokenStore = tokenStore;
        _sync = new OutboxSyncService(outbox, api);
        _email = email;
        _collector = new WindowedIntervalCollector(email, settings.Tracking.WindowSeconds);

        Width = 460;
        Height = 432;
        int contentW = Width - Pad * 2;

        // ---- header ----
        var appName = new Label
        {
            Text = "TimeTrack", Font = Theme.FontAppName, ForeColor = Theme.TextPrimary,
            AutoSize = true, BackColor = Color.Transparent, Left = Pad, Top = 18
        };
        var userEmail = new Label
        {
            Text = _email, Font = Theme.FontBody, ForeColor = Theme.TextSecondary,
            AutoSize = true, BackColor = Color.Transparent, Top = 19
        };
        Controls.Add(appName);
        Controls.Add(userEmail);
        userEmail.Left = Width - Pad - userEmail.PreferredWidth;
        EnableDrag(appName);
        EnableDrag(this);

        // ---- status pill ----
        _statusPill = new Panel { Top = 52, Height = 30, BackColor = Color.Transparent };
        _statusPill.Paint += PaintStatusPill;
        Controls.Add(_statusPill);

        // ---- timer ----
        _timer = new Label
        {
            Text = "00:00:00", Font = Theme.FontDisplayTimer, ForeColor = Theme.TextPrimary,
            AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent,
            Left = 0, Top = 92, Width = Width, Height = 56
        };
        _timerCaption = new Label
        {
            Text = "Total work time today", Font = Theme.FontCaption, ForeColor = Theme.TextSecondary,
            AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent,
            Left = 0, Top = 150, Width = Width, Height = 18
        };
        Controls.Add(_timer);
        Controls.Add(_timerCaption);
        EnableDrag(_timer);

        // ---- timeline ----
        var tlStart = new Label
        {
            Text = DateTime.Now.ToString("h:mm tt"), Font = Theme.FontCaption, ForeColor = Theme.TextSecondary,
            AutoSize = true, BackColor = Color.Transparent, Left = Pad, Top = 184
        };
        var tlNow = new Label
        {
            Text = "Now", Font = Theme.FontCaption, ForeColor = Theme.TextSecondary,
            AutoSize = true, BackColor = Color.Transparent, Top = 184
        };
        Controls.Add(tlStart);
        Controls.Add(tlNow);
        tlNow.Left = Width - Pad - tlNow.PreferredWidth;

        _timeline = new Panel { Left = Pad, Top = 206, Width = contentW, Height = 14, BackColor = Color.Transparent };
        _timeline.Paint += PaintTimeline;
        Controls.Add(_timeline);

        Controls.Add(Legend(Pad, 230));

        // ---- stat cards ----
        int gap = 12;
        int cardW = (contentW - gap * 2) / 3;
        int cardsTop = 258;
        var cardLogged = StatCard(Pad, cardsTop, cardW, "Logged in", DateTime.Now.ToString("h:mm tt"), Theme.CardFill, Theme.TextPrimary, out _);
        _cardBreaks = StatCard(Pad + cardW + gap, cardsTop, cardW, "Breaks", "0:00 · 0", Theme.CardFill, Theme.TextPrimary, out _breaksValue);
        var cardIdle = StatCard(Pad + (cardW + gap) * 2, cardsTop, cardW, "Idle", "0:00", Theme.CardFill, Theme.TextPrimary, out _);
        Controls.Add(cardLogged);
        Controls.Add(_cardBreaks);
        Controls.Add(cardIdle);

        // ---- primary + logout buttons ----
        int btnTop = 338;
        int btnH = 44;
        int breakW = (int)((contentW - gap) * 1.6 / 2.6);
        int logoutW = contentW - gap - breakW;

        _btnBreak = new RoundedButton
        {
            Text = "Take a break", FillColor = Theme.BreakSurface, ForeColor = Theme.BreakText,
            Radius = Theme.RadiusCard, Left = Pad, Top = btnTop, Width = breakW, Height = btnH
        };
        _btnBreak.Click += (_, _) => ToggleBreak();

        _btnLogout = new RoundedButton
        {
            Text = "Log out", Outline = true, OutlineColor = Theme.Border, ForeColor = Theme.TextSecondary,
            Radius = Theme.RadiusCard, Left = Pad + breakW + gap, Top = btnTop, Width = logoutW, Height = btnH
        };
        _btnLogout.Click += async (_, _) => await LogoutAndSyncAsync();

        Controls.Add(_btnBreak);
        Controls.Add(_btnLogout);

        // ---- offline / queue note ----
        _offlineNote = new Label
        {
            Text = "Offline · your data syncs automatically when you log out",
            Font = Theme.FontCaption, ForeColor = Theme.TextMuted, AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent,
            Left = 0, Top = 396, Width = Width, Height = 18
        };
        Controls.Add(_offlineNote);

        UpdateStatusPill();
        UpdateTimerDisplay();

        _tick.Tick += OnTick;
        _tick.Start();

        FormClosed += (_, _) => _tick.Dispose();

        _ = UpdatePendingAsync();
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        // Real activity sampling: idle = no input for the configured threshold.
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
        _timeline.Invalidate();
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
            // Close any partial window so the last minutes aren't lost.
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
                // NOTE: Phase 5 replaces this MessageBox with the internet-window + sync dialogs (06–09).
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
            Close();
        }
    }

    private void ToggleBreak()
    {
        _onBreak = !_onBreak;

        if (_onBreak)
        {
            _btnBreak.Text = "Resume work";
            _btnBreak.FillColor = Theme.WorkPrimary;
            _btnBreak.ForeColor = Color.White;
            _timer.ForeColor = Theme.TextMuted;
            _timerCaption.Text = "Total work time · paused";
            _cardBreaks.FillColor = Theme.BreakSurface;
            _breaksValue.ForeColor = Theme.BreakText;
        }
        else
        {
            _btnBreak.Text = "Take a break";
            _btnBreak.FillColor = Theme.BreakSurface;
            _btnBreak.ForeColor = Theme.BreakText;
            _timer.ForeColor = Theme.TextPrimary;
            _timerCaption.Text = "Total work time today";
            _cardBreaks.FillColor = Theme.CardFill;
            _breaksValue.ForeColor = Theme.TextPrimary;
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
        catch
        {
            // non-fatal UI nicety
        }
    }

    private void UpdateTimerDisplay()
    {
        var ts = TimeSpan.FromSeconds(_workSeconds);
        _timer.Text = $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
    }

    private void UpdateBreaksValue()
    {
        var ts = TimeSpan.FromSeconds(_breakSeconds);
        _breaksValue.Text = $"{(int)ts.TotalHours}:{ts.Minutes:00} · {(_breakSeconds > 0 ? 1 : 0)}";
    }

    private void UpdateStatusPill()
    {
        string text = _onBreak
            ? $"On break · {_breakSeconds / 60}:{_breakSeconds % 60:00}"
            : "Working";
        int w = TextRenderer.MeasureText(text, Theme.FontPill).Width + 44;
        _statusPill.Width = w;
        _statusPill.Left = (Width - w) / 2;
        _statusPill.Invalidate();
    }

    // ---- custom paint ----
    private void PaintStatusPill(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color bg = _onBreak ? Theme.BreakSurface : Theme.WorkPillBg;
        Color fg = _onBreak ? Theme.BreakText : Theme.WorkPillText;
        Color dot = _onBreak ? Theme.BreakSegment : Theme.WorkPrimary;
        string text = _onBreak ? $"On break · {_breakSeconds / 60}:{_breakSeconds % 60:00}" : "Working";

        var r = new Rectangle(0, 0, _statusPill.Width - 1, _statusPill.Height - 1);
        using (var path = Draw.RoundedRect(r, r.Height / 2))
        using (var b = new SolidBrush(bg))
            g.FillPath(b, path);

        using (var d = new SolidBrush(dot))
            g.FillEllipse(d, 14, r.Height / 2 - 4, 8, 8);

        var textRect = new Rectangle(26, 0, r.Width - 30, r.Height);
        TextRenderer.DrawText(g, text, Theme.FontPill, textRect, fg,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private void PaintTimeline(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int total = Math.Max(1, _workSeconds + _breakSeconds + _idleSeconds);
        int w = _timeline.Width;
        int h = _timeline.Height;

        using var path = Draw.RoundedRect(new Rectangle(0, 0, w, h), Theme.RadiusBar);

        using (var track = new SolidBrush(Theme.CardFill))
            g.FillPath(track, path);

        var prevClip = g.Clip;
        g.SetClip(path);

        int workW = (int)(w * (_workSeconds / (double)total));
        int breakW = (int)(w * (_breakSeconds / (double)total));
        int idleW = w - workW - breakW;

        int x = 0;
        FillSegment(g, x, h, workW, Theme.WorkPrimary); x += workW;
        FillSegment(g, x, h, breakW, Theme.BreakSegment); x += breakW;
        FillSegment(g, x, h, idleW, Theme.IdleSegment);

        g.Clip = prevClip;
    }

    private static void FillSegment(Graphics g, int x, int h, int w, Color color)
    {
        if (w <= 0) return;
        using var b = new SolidBrush(color);
        g.FillRectangle(b, x, 0, w, h);
    }

    private static Control Legend(int left, int top)
    {
        var p = new FlowLayoutPanel
        {
            Left = left, Top = top, AutoSize = true, BackColor = Color.Transparent, WrapContents = false
        };
        p.Controls.Add(LegendItem("Work", Theme.WorkPrimary));
        p.Controls.Add(LegendItem("Break", Theme.BreakSegment));
        p.Controls.Add(LegendItem("Idle", Theme.IdleSegment));
        return p;
    }

    private static Control LegendItem(string text, Color color)
    {
        var item = new Label
        {
            Text = "  " + text, Font = Theme.FontCaption, ForeColor = Theme.TextSecondary,
            AutoSize = true, BackColor = Color.Transparent, Padding = new Padding(14, 0, 8, 0)
        };
        item.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var b = new SolidBrush(color);
            e.Graphics.FillEllipse(b, 0, item.Height / 2 - 4, 8, 8);
        };
        return item;
    }

    private static RoundedPanel StatCard(int left, int top, int width, string caption, string value,
        Color fill, Color valueColor, out Label valueLabel)
    {
        var card = new RoundedPanel
        {
            Left = left, Top = top, Width = width, Height = 64,
            Radius = Theme.RadiusCard, FillColor = fill, DrawBorder = false
        };
        valueLabel = new Label
        {
            Text = value, Font = Theme.FontValue, ForeColor = valueColor,
            AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent,
            Left = 0, Top = 12, Width = width, Height = 24
        };
        var capLabel = new Label
        {
            Text = caption, Font = Theme.FontCaption, ForeColor = Theme.TextSecondary,
            AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent,
            Left = 0, Top = 38, Width = width, Height = 16
        };
        card.Controls.Add(valueLabel);
        card.Controls.Add(capLabel);
        return card;
    }
}
