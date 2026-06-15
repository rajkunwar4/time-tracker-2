using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TimeTrack.App.Ui;

namespace TimeTrack.App.Forms;

/// <summary>
/// Screen 01 — Login. Email + password only. There is no "start work" button:
/// tracking begins automatically the moment sign-in succeeds and this window closes.
/// </summary>
internal sealed class FrmLogin : AppForm
{
    private const int Pad = 28;

    private readonly TextBox _email;
    private readonly TextBox _password;

    /// <summary>The signed-in user's email (valid only when DialogResult == OK).</summary>
    public string Email { get; private set; } = string.Empty;

    public FrmLogin()
    {
        Width = 360;
        Height = 396;
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
        _email = Input(emailPanel, "priya@company.com", isPassword: false);
        Controls.Add(emailPanel);

        // ---- password (with reveal toggle) ----
        y += 70;
        Controls.Add(Caption("Password", Pad, y));
        var pwPanel = Field(Pad, y + 18, contentW);
        _password = Input(pwPanel, "Password", isPassword: true);
        _password.Width -= 30; // leave room for the eye toggle

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

        // ---- sign in ----
        y += 76;
        var signIn = new RoundedButton
        {
            Text = "Sign in",
            FillColor = Theme.WorkPrimary,
            Radius = Theme.RadiusCard,
            Left = Pad,
            Top = y,
            Width = contentW,
            Height = 44
        };
        signIn.Click += (_, _) => TrySignIn();
        Controls.Add(signIn);
        AcceptButton = signIn;

        // ---- footnote ----
        var note = CenteredLabel("Time tracking starts automatically after you sign in",
            Theme.FontCaption, Theme.TextMuted, y + 56, 18);
        EnableDrag(note);

        EnableDrag(this);
    }

    private void TrySignIn()
    {
        // Phase 4 will validate against cached credentials / the API.
        // For now: require both fields to be present, then start tracking.
        if (string.IsNullOrWhiteSpace(_email.Text) || string.IsNullOrWhiteSpace(_password.Text))
        {
            return;
        }

        Email = _email.Text.Trim();
        DialogResult = DialogResult.OK;
        Close();
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
