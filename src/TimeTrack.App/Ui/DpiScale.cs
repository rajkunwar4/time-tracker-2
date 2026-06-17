using System.Runtime.InteropServices;

namespace TimeTrack.App.Ui;

/// <summary>
/// A single process-wide DPI scale factor, captured once at startup from the primary
/// monitor. Every pixel dimension (fonts, sizes, paddings, radii) is multiplied by this
/// so the whole UI is authored in logical 96-DPI units but rendered crisply at the
/// machine's actual scale. WinForms auto-scaling is left off so this is the only
/// scaling applied — no double-scaling surprises.
/// </summary>
internal static class DpiScale
{
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("Shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hmon, int dpiType, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr hdc, int index);

    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const int MDT_EFFECTIVE_DPI = 0;
    private const int LOGPIXELSX = 88;

    /// <summary>Scale factor (1.0 at 96 DPI, 1.25 at 120 DPI, 1.5 at 144 DPI, …).</summary>
    public static float Factor { get; } = Probe();

    private static float Probe()
    {
        // Per-monitor effective DPI of the primary monitor. Under PerMonitorV2 a screen
        // DC reports only the *system* DPI (often 96), so GetDpiForMonitor is required to
        // see the monitor's true scale.
        try
        {
            IntPtr mon = MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY);
            if (mon != IntPtr.Zero && GetDpiForMonitor(mon, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
                return dpiX / 96f;
        }
        catch { /* fall back below (e.g. Shcore unavailable) */ }

        IntPtr dc = GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return 1f;
        try
        {
            int dpi = GetDeviceCaps(dc, LOGPIXELSX);
            return dpi <= 0 ? 1f : dpi / 96f;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
    }

    /// <summary>Scale a logical (96-DPI) pixel value to the device.</summary>
    public static int Scale(int logical) => (int)Math.Round(logical * Factor);

    /// <summary>Scale a logical pixel value, as a float (for font sizes).</summary>
    public static float ScaleF(float logical) => logical * Factor;
}
