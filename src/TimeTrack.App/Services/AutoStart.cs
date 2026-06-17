using System.Windows.Forms;
using Microsoft.Win32;

namespace TimeTrack.App.Services;

/// <summary>
/// Registers (or removes) the app under the per-user logon Run key so it launches
/// automatically at Windows sign-in. Per-user (HKCU) — needs no admin rights, matching
/// the "non-admin client app" design. Best-effort: failures are swallowed.
/// </summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TimeTrack";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return;

            if (enabled)
            {
                var exe = Environment.ProcessPath ?? Application.ExecutablePath;
                var desired = $"\"{exe}\"";
                if (key.GetValue(ValueName) as string != desired)
                    key.SetValue(ValueName, desired, RegistryValueKind.String);
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Auto-start is best-effort; never block startup on it.
        }
    }
}
