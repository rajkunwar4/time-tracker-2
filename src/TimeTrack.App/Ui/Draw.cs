using System.Drawing;
using System.Drawing.Drawing2D;

namespace TimeTrack.App.Ui;

internal static class Draw
{
    /// <summary>Build a rounded-rectangle path. Degrades to a plain rectangle when radius &lt;= 0.</summary>
    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(r);
            path.CloseFigure();
            return path;
        }

        // Clamp the radius so it never exceeds half the smaller side.
        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
