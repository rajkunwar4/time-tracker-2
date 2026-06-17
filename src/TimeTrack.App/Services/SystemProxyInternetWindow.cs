using System.Runtime.InteropServices;
using Microsoft.Win32;
using TimeTrack.Core.Configuration;
using TimeTrack.Core.Sync;

namespace TimeTrack.App.Services;

/// <summary>
/// Opens a time-boxed, whole-PC internet window by toggling the per-user Windows system
/// proxy to the master gateway (e.g. 192.168.137.1:808) — exactly the "Use a proxy server"
/// switch employees flip by hand. Per-user (HKCU) ⇒ no admin.
///
/// <para>Failsafe: the window auto-closes after <c>MaxWindowMinutes</c> and on process exit,
/// so a crash can never leave the whole machine's internet open.</para>
/// </summary>
internal sealed class SystemProxyInternetWindow : IInternetWindow, IDisposable
{
    private const string InternetSettings = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    private const int INTERNET_OPTION_REFRESH = 37;

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    private readonly string _proxyServer;
    private readonly TimeSpan _maxWindow;
    private readonly object _gate = new();
    private System.Threading.Timer? _failsafe;
    private bool _disposed;

    public SystemProxyInternetWindow(ProxySettings settings)
    {
        _proxyServer = $"{settings.Address}:{settings.Port}";
        _maxWindow = TimeSpan.FromMinutes(Math.Max(1, settings.MaxWindowMinutes));
        // Failsafe: clear the proxy even on an abrupt process exit.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => SetProxy(false);
    }

    public Task OpenAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            SetProxy(true);
            _failsafe?.Dispose();
            _failsafe = new System.Threading.Timer(_ => CloseInternal(), null, _maxWindow, Timeout.InfiniteTimeSpan);
        }
        return Task.CompletedTask;
    }

    public Task CloseAsync(CancellationToken ct = default)
    {
        CloseInternal();
        return Task.CompletedTask;
    }

    private void CloseInternal()
    {
        lock (_gate)
        {
            _failsafe?.Dispose();
            _failsafe = null;
            SetProxy(false);
        }
    }

    private void SetProxy(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettings, writable: true);
            if (key is null) return;
            if (enabled)
                key.SetValue("ProxyServer", _proxyServer, RegistryValueKind.String);
            key.SetValue("ProxyEnable", enabled ? 1 : 0, RegistryValueKind.DWord);
            // Tell WinINET/WinHTTP to pick up the change immediately (no restart needed).
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
        catch
        {
            // Best-effort: never let a registry/WinINET hiccup crash the sync flow.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseInternal();   // never leave the proxy enabled
    }
}
