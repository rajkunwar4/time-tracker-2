using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TimeTrack.App.Ui;
using TimeTrack.Core.Api;
using TimeTrack.Core.Security;

namespace TimeTrack.App.Forms;

/// <summary>
/// Screen 01 — Login. Email + password only. On a successful API login the JWT is
/// stored (DPAPI) and the window closes; tracking then starts automatically.
///
/// <para>Built with a single-column <see cref="TableLayoutPanel"/>; vertical rhythm comes
/// from control margins, not absolute coordinates, so it reflows and DPI-scales cleanly.</para>
/// </summary>
internal sealed class FrmLogin : AppForm
{
    private const int DesignWidth = 360;

    private readonly TimeTrackApiClient _api;
    private readonly ITokenStore _tokenStore;

    private readonly TableLayoutPanel _root;
    private readonly TextBox _email;
    private readonly TextBox _password;
    private readonly RoundedButton _signIn;
    private readonly Label _error;

    /// <summary>Raised with the signed-in user's email after a successful login.</summary>
    public event Action<string>? LoginSucceeded;

    public FrmLogin(TimeTrackApiClient api, ITokenStore tokenStore)
    {
        _api = api;
        _tokenStore = tokenStore;

        ClientSize = new Size(Dpi(DesignWidth), Dpi(440));

        _root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = Theme.Surface,
            Padding = new Padding(Dpi(28), Dpi(26), Dpi(28), Dpi(24))
        };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Dpi(DesignWidth - 56))); // 28px padding each side

        // ---- logo tile (clock glyph) ----
        var logo = new Panel
        {
            Width = Dpi(48),
            Height = Dpi(48),
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 0, Dpi(14))
        };
        logo.Paint += PaintLogo;
        EnableDrag(logo);

        // ---- title + subtitle ----
        var appName = Centered("TimeTrack", Theme.FontAppNameLg, Theme.TextPrimary, new Padding(0, 0, 0, Dpi(2)));
        var subtitle = Centered("Sign in to start your day", Theme.FontBody, Theme.TextSecondary, new Padding(0, 0, 0, Dpi(22)));
        EnableDrag(appName);
        EnableDrag(subtitle);

        // ---- email ----
        var emailCaption = Caption("Email");
        var emailField = Field(out _email, "you@company.com", isPassword: false, trailing: null);
        emailField.Margin = new Padding(0, Dpi(6), 0, Dpi(16));

        // ---- password (with reveal toggle) ----
        var passwordCaption = Caption("Password");
        var toggle = new RoundedButton
        {
            Outline = true,
            OutlineColor = Color.Transparent,
            ForeColor = Theme.TextSecondary,
            Font = Theme.FontCaption,
            Text = "show",
            Width = Dpi(42),
            Dock = DockStyle.Right,
            Margin = Padding.Empty,
            TabStop = false   // reveal toggle shouldn't be in the tab order (email → password → sign in)
        };
        var passwordField = Field(out _password, "Password", isPassword: true, trailing: toggle);
        passwordField.Margin = new Padding(0, Dpi(6), 0, Dpi(10));
        toggle.Click += (_, _) =>
        {
            _password.UseSystemPasswordChar = !_password.UseSystemPasswordChar;
            toggle.Text = _password.UseSystemPasswordChar ? "show" : "hide";
            _password.Focus();
        };

        // ---- error line (reserves its row height) ----
        _error = new Label
        {
            Text = string.Empty,
            Font = Theme.FontCaption,
            ForeColor = Theme.DangerAccent,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = Dpi(18),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, Dpi(6))
        };

        // ---- sign in ----
        _signIn = new RoundedButton
        {
            Text = "Sign in",
            FillColor = Theme.WorkPrimary,
            ForeColor = Color.White,
            Radius = Theme.RadiusCard,
            Dock = DockStyle.Fill,
            Height = Dpi(44),
            Margin = new Padding(0, 0, 0, Dpi(16))
        };
        _signIn.Click += async (_, _) => await TrySignInAsync();
        AcceptButton = _signIn;

        // ---- footnote ----
        var note = Centered("Time tracking starts automatically after you sign in",
            Theme.FontCaption, Theme.TextMuted, Padding.Empty);
        EnableDrag(note);

        foreach (var c in new Control[] { logo, appName, subtitle, emailCaption, emailField,
                     passwordCaption, passwordField, _error, _signIn, note })
            _root.Controls.Add(c);

        // Pin the card to the design width; height auto-sizes to content.
        _root.MinimumSize = new Size(Dpi(DesignWidth), 0);
        _root.MaximumSize = new Size(Dpi(DesignWidth), 0);
        MountCentered(_root);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        LockMinimumToContent(_root, DesignWidth);
        _email.Focus();
    }

    private async Task TrySignInAsync()
    {
        var email = _email.Text.Trim();
        var password = _password.Text;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            _error.Text = "Enter your email and password.";
            return;
        }

        _error.Text = string.Empty;
        _signIn.Enabled = false;
        _signIn.Text = "Signing in…";
        bool ok = false;
        try
        {
            var result = await _api.LoginAsync(email, password);

            if (result.Success)
            {
                // Online: persist the session token and cache a verifier for future offline sign-ins.
                _tokenStore.Save(new StoredToken
                {
                    Token = result.Token,
                    Email = result.Email,
                    EmployeeId = result.EmployeeId,
                    Role = result.Role,
                    ObtainedUtc = DateTime.UtcNow,
                    PasswordVerifier = PasswordVerifier.Hash(password)
                });
                ok = true;
                LoginSucceeded?.Invoke(result.Email);
            }
            else if (result.Unreachable && TryOfflineLogin(email, password, out var offlineEmail))
            {
                // Server unreachable but the credentials match the cached verifier → track locally.
                ok = true;
                LoginSucceeded?.Invoke(offlineEmail);
            }
            else if (result.Unreachable)
            {
                _error.Text = "Can't reach the server, and no saved offline sign-in for this account.";
            }
            else
            {
                _error.Text = result.Error ?? "Sign in failed.";
            }
        }
        finally
        {
            if (!ok)
            {
                _signIn.Enabled = true;
                _signIn.Text = "Sign in";
            }
        }
    }

    /// <summary>
    /// Validate credentials against the locally cached verifier (set on the last online login).
    /// Only the last online-signed-in account on this PC can sign in offline.
    /// </summary>
    private bool TryOfflineLogin(string email, string password, out string cachedEmail)
    {
        cachedEmail = string.Empty;
        var cached = _tokenStore.Load();
        if (cached is null || string.IsNullOrEmpty(cached.PasswordVerifier)) return false;
        if (!string.Equals(cached.Email, email, StringComparison.OrdinalIgnoreCase)) return false;
        if (!PasswordVerifier.Verify(password, cached.PasswordVerifier)) return false;

        cachedEmail = cached.Email;
        return true;
    }

    // ---- builders ----
    private Label Centered(string text, Font font, Color color, Padding margin) => new()
    {
        Text = text,
        Font = font,
        ForeColor = color,
        AutoSize = false,
        Dock = DockStyle.Fill,
        Height = font.Height + Dpi(2),
        TextAlign = ContentAlignment.MiddleCenter,
        BackColor = Color.Transparent,
        Margin = margin
    };

    private Label Caption(string text) => new()
    {
        Text = text,
        Font = Theme.FontCaption,
        ForeColor = Theme.TextSecondary,
        AutoSize = true,
        BackColor = Color.Transparent,
        Margin = new Padding(Dpi(2), 0, 0, 0)
    };

    private RoundedPanel Field(out TextBox box, string placeholder, bool isPassword, RoundedButton? trailing)
    {
        var host = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Height = Dpi(40),
            Radius = Theme.RadiusCard,
            FillColor = Theme.Surface,
            BorderColor = Theme.Border,
            Padding = new Padding(Dpi(12), 0, Dpi(8), 0)
        };

        var input = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Font = Theme.FontBody,
            ForeColor = Theme.TextPrimary,
            BackColor = Theme.Surface,
            PlaceholderText = placeholder,
            UseSystemPasswordChar = isPassword
        };

        // Vertically centre the (single-line, non-dockable-height) textbox inside the host.
        var center = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        center.Controls.Add(input);
        input.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        void CenterInput()
        {
            input.Top = Math.Max(0, (center.Height - input.Height) / 2);
            input.Left = 0;
            input.Width = center.Width;
        }
        center.Resize += (_, _) => CenterInput();
        center.HandleCreated += (_, _) => CenterInput();

        if (trailing != null) host.Controls.Add(trailing);
        host.Controls.Add(center);
        box = input;
        return host;
    }

    private void PaintLogo(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var tile = new Rectangle(0, 0, Dpi(48) - 1, Dpi(48) - 1);
        using (var p = Draw.RoundedRect(tile, Theme.RadiusCard + Dpi(4)))
        using (var b = new SolidBrush(Theme.WorkPillBg))
            g.FillPath(b, p);

        // simple clock glyph
        int cx = tile.Width / 2, cy = tile.Height / 2, r = Dpi(11);
        using var pen = new Pen(Theme.WorkDeep, Dpi(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
        g.DrawLine(pen, cx, cy, cx, cy - r + Dpi(4));          // hour hand (up)
        g.DrawLine(pen, cx, cy, cx + r - Dpi(5), cy + Dpi(2)); // minute hand
    }
}
