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
/// </summary>
internal sealed class FrmLogin : AppForm
{
    private const int Pad = 28;

    private readonly TimeTrackApiClient _api;
    private readonly ITokenStore _tokenStore;

    private readonly TextBox _email;
    private readonly TextBox _password;
    private readonly RoundedButton _signIn;
    private readonly Label _error;

    /// <summary>The signed-in user's email (valid only when DialogResult == OK).</summary>
    public string Email { get; private set; } = string.Empty;

    public FrmLogin(TimeTrackApiClient api, ITokenStore tokenStore)
    {
        _api = api;
        _tokenStore = tokenStore;

        Width = 360;
        Height = 420;
        int contentW = Width - Pad * 2;

        // ---- logo tile + app name ----
        var logo = new Panel { Width = 48, Height = 48, Left = (Width - 48) / 2, Top = 28, BackColor = Color.Transparent };
        logo.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var p = Draw.RoundedRect(new Rectangle(0, 0, 47, 47), Theme.RadiusCard);
            using var b = new SolidBrush(Theme.WorkPillBg);
            e.Graphics.FillPath(b, p);
            TextRenderer.DrawText(e.Graphics, "TT", Theme.FontAppNameLg, new Rectangle(0, 0, 48, 48),
                Theme.WorkDeep, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        Controls.Add(logo);
        EnableDrag(logo);

        var appName = CenteredLabel("TimeTrack", Theme.FontAppNameLg, Theme.TextPrimary, 86, 26);
        var subtitle = CenteredLabel("Sign in to start your day", Theme.FontBody, Theme.TextSecondary, 114, 18);
        EnableDrag(appName);
        EnableDrag(subtitle);

        // ---- email ----
        int y = 152;
        Controls.Add(Caption("Email", Pad, y));
        var emailPanel = Field(Pad, y + 18, contentW);
        _email = Input(emailPanel, "you@company.com", isPassword: false);
        Controls.Add(emailPanel);

        // ---- password (with reveal toggle) ----
        y += 70;
        Controls.Add(Caption("Password", Pad, y));
        var pwPanel = Field(Pad, y + 18, contentW);
        _password = Input(pwPanel, "Password", isPassword: true);
        _password.Width -= 30;

        var eye = new RoundedButton
        {
            Outline = true,
            OutlineColor = Color.Transparent,
            ForeColor = Theme.TextSecondary,
            Font = Theme.FontBody,
            Text = "show",
            Width = 40,
            Height = 24,
            Top = (pwPanel.Height - 24) / 2,
            Left = pwPanel.Width - 46
        };
        eye.Click += (_, _) =>
        {
            _password.UseSystemPasswordChar = !_password.UseSystemPasswordChar;
            eye.Text = _password.UseSystemPasswordChar ? "show" : "hide";
            _password.Focus();
        };
        pwPanel.Controls.Add(eye);
        Controls.Add(pwPanel);

        // ---- error line ----
        _error = new Label
        {
            Text = string.Empty,
            Font = Theme.FontCaption,
            ForeColor = Theme.DangerAccent,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Left = Pad,
            Top = y + 64,
            Width = contentW,
            Height = 16
        };
        Controls.Add(_error);

        // ---- sign in ----
        y += 86;
        _signIn = new RoundedButton
        {
            Text = "Sign in",
            FillColor = Theme.WorkPrimary,
            Radius = Theme.RadiusCard,
            Left = Pad,
            Top = y,
            Width = contentW,
            Height = 44
        };
        _signIn.Click += async (_, _) => await TrySignInAsync();
        Controls.Add(_signIn);
        AcceptButton = _signIn;

        // ---- footnote ----
        var note = CenteredLabel("Time tracking starts automatically after you sign in",
            Theme.FontCaption, Theme.TextMuted, y + 56, 18);
        EnableDrag(note);

        EnableDrag(this);
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
        try
        {
            var result = await _api.LoginAsync(email, password);
            if (!result.Success)
            {
                _error.Text = result.Error ?? "Sign in failed.";
                return;
            }

            _tokenStore.Save(new StoredToken
            {
                Token = result.Token,
                Email = result.Email,
                EmployeeId = result.EmployeeId,
                Role = result.Role,
                ObtainedUtc = DateTime.UtcNow
            });

            Email = result.Email;
            DialogResult = DialogResult.OK;
            Close();
        }
        finally
        {
            if (DialogResult != DialogResult.OK)
            {
                _signIn.Enabled = true;
                _signIn.Text = "Sign in";
            }
        }
    }

    // ---- small layout helpers ----
    private Label CenteredLabel(string text, Font font, Color color, int top, int height)
    {
        var l = new Label
        {
            Text = text,
            Font = font,
            ForeColor = color,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Left = 0,
            Top = top,
            Width = Width,
            Height = height
        };
        Controls.Add(l);
        return l;
    }

    private static Label Caption(string text, int left, int top) => new()
    {
        Text = text,
        Font = Theme.FontCaption,
        ForeColor = Theme.TextSecondary,
        AutoSize = true,
        BackColor = Color.Transparent,
        Left = left,
        Top = top
    };

    private static RoundedPanel Field(int left, int top, int width) => new()
    {
        Left = left,
        Top = top,
        Width = width,
        Height = 40,
        Radius = Theme.RadiusCard,
        FillColor = Theme.Surface,
        BorderColor = Theme.Border
    };

    private static TextBox Input(RoundedPanel host, string placeholder, bool isPassword)
    {
        var tb = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Font = Theme.FontBody,
            ForeColor = Theme.TextPrimary,
            BackColor = host.FillColor,
            PlaceholderText = placeholder,
            Left = 12,
            Top = (host.Height - Theme.FontBody.Height) / 2,
            Width = host.Width - 24,
            UseSystemPasswordChar = isPassword
        };
        host.Controls.Add(tb);
        return tb;
    }
}
