using System.Drawing;

namespace TimeTrack.App;

/// <summary>
/// Design tokens from the UI spec. One palette, one type family (Segoe UI),
/// three tracked states. Colours encode meaning, never decoration.
/// </summary>
internal static class Theme
{
    // ---- Colour: core (work / break / idle) ----
    public static readonly Color WorkPrimary = Hex("1D9E75"); // work segments, status dot, primary buttons
    public static readonly Color WorkDeep    = Hex("0F6E56"); // deep accents
    public static readonly Color WorkPillBg  = Hex("E1F5EE"); // "Working" pill background
    public static readonly Color WorkPillText = Hex("085041"); // "Working" pill text
    public static readonly Color BreakSegment = Hex("FAC775"); // break segments on timeline
    public static readonly Color BreakSurface = Hex("FAEEDA"); // break button / break-state cards
    public static readonly Color BreakText    = Hex("633806");
    public static readonly Color BreakTextAlt = Hex("854F0B");
    public static readonly Color IdleSegment  = Hex("B4B2A9"); // idle segments (grey = inactive)

    // ---- Colour: neutrals & status ----
    public static readonly Color Surface      = Hex("FFFFFF"); // window & dialog background
    public static readonly Color CardFill     = Hex("F1EFE8"); // stat-card / note-box background
    public static readonly Color Border       = Hex("D3D1C7"); // borders, outline buttons, dividers
    public static readonly Color TextPrimary  = Hex("2C2C2A"); // timer, titles, values
    public static readonly Color TextSecondary = Hex("888780"); // captions, labels, email
    public static readonly Color TextMuted    = Hex("B4B2A9"); // footnotes, hints, paused timer
    public static readonly Color DangerBg     = Hex("FCEBEB"); // sync-failed halo
    public static readonly Color DangerAccent = Hex("A32D2D");
    public static readonly Color DarkStrip    = Hex("1E1E1E"); // logging countdown strip
    public static readonly Color StripAccent  = Hex("5DCAA5");
    public static readonly Color StripAmber   = Hex("EF9F27"); // strip bar under 1:00

    // ---- Radii (px) ----
    public const int RadiusWindow = 8;
    public const int RadiusDialog = 10;
    public const int RadiusCard   = 6;
    public const int RadiusBar    = 4;

    // ---- Typography (Segoe UI; sizes in px) ----
    // 500-weight tokens use "Segoe UI Semibold"; body/caption use regular.
    public static readonly Font FontDisplayTimer = Semibold(44f);
    public static readonly Font FontDialogTitle  = Semibold(15f);
    public static readonly Font FontStripTimer   = Semibold(16f);
    public static readonly Font FontAppNameLg    = Semibold(17f); // login header
    public static readonly Font FontAppName      = Semibold(13f); // main header
    public static readonly Font FontValue        = Semibold(15f);
    public static readonly Font FontPill         = Semibold(12f);
    public static readonly Font FontButton       = Semibold(14f);
    public static readonly Font FontBody         = Regular(12f);
    public static readonly Font FontCaption      = Regular(11f);

    private static Font Semibold(float px) => new("Segoe UI Semibold", px, FontStyle.Regular, GraphicsUnit.Pixel);
    private static Font Regular(float px)  => new("Segoe UI", px, FontStyle.Regular, GraphicsUnit.Pixel);

    public static Color Hex(string hex)
    {
        hex = hex.TrimStart('#');
        return Color.FromArgb(
            Convert.ToInt32(hex.Substring(0, 2), 16),
            Convert.ToInt32(hex.Substring(2, 2), 16),
            Convert.ToInt32(hex.Substring(4, 2), 16));
    }
}
