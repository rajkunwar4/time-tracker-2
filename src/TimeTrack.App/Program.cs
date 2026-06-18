using System.Net;
using System.Net.Http;
using System.Windows.Forms;
using TimeTrack.App.Services;
using TimeTrack.Core.Api;
using TimeTrack.Core.Configuration;
using TimeTrack.Core.Storage;
using TimeTrack.Core.Sync;

namespace TimeTrack.App;

internal static class Program
{
    private static Mutex? _mutex;
    private const string MutexName = "TimeTrack_SingleInstance_Mutex";
    private const string ShowWindowSignalName = "TimeTrack_ShowWindow_Event";

    [STAThread]
    private static void Main()
    {
        // Single instance — only one tracker per machine session.
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Already running (the live instance sits in the tray). Signal it to surface its
            // window, then exit silently — no "already running" dialog for the user to dismiss.
            if (EventWaitHandle.TryOpenExisting(ShowWindowSignalName, out var existing))
            {
                existing.Set();
                existing.Dispose();
            }
            return;
        }

        // Primary instance: the signal that any later launch sets to ask us to show our window.
        var showWindowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowSignalName);

        ApplicationConfiguration.Initialize();

        // ---- compose services ----
        var baseDir = AppContext.BaseDirectory;
        var dataDir = Path.Combine(baseDir, "Data");
        Directory.CreateDirectory(dataDir);

        var settings = AppSettings.Load(Path.Combine(baseDir, "appsettings.json"));

        // Register (or remove) auto-start at Windows sign-in.
        // Debug builds never auto-launch (dev convenience) and clear any stale key;
        // shipped Release builds honor the appsettings toggle.
#if DEBUG
        AutoStart.Apply(false);
#else
        AutoStart.Apply(settings.Startup.RunAtLogon);
#endif

        var outbox = new SqliteOutboxRepository(Path.Combine(dataDir, "timetrack.db"));
        outbox.InitializeAsync().GetAwaiter().GetResult(); // one-time, at startup

        // Internet window: in Release with Proxy.Enabled, route the app's traffic through the
        // master proxy and toggle the whole-PC system proxy around syncs. Debug builds stay
        // direct (NoOp) so a dev machine's browsing is never disrupted.
#if DEBUG
        bool proxyActive = false;
#else
        bool proxyActive = settings.Proxy.Enabled;
#endif

        HttpClient? http = null;
        if (proxyActive)
        {
            var handler = new HttpClientHandler
            {
                Proxy = new WebProxy($"{settings.Proxy.Address}:{settings.Proxy.Port}") { BypassProxyOnLocal = true },
                UseProxy = true
            };
            http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(settings.Api.TimeoutSeconds) };
        }

        var api = new TimeTrackApiClient(settings.Api.BaseUrl, http, settings.Api.TimeoutSeconds);
        var tokenStore = new DpapiTokenStore(Path.Combine(dataDir, "token.bin"));

        IInternetWindow internetWindow = proxyActive
            ? new SystemProxyInternetWindow(settings.Proxy)
            : new NoOpInternetWindow();

        try
        {
            // The context owns the always-on lifecycle: login ⇄ main, minimize-to-tray.
            Application.Run(new TrackerAppContext(settings, outbox, api, tokenStore, internetWindow, showWindowSignal));
        }
        finally
        {
            (internetWindow as IDisposable)?.Dispose();   // failsafe: clear the proxy on exit
            showWindowSignal.Dispose();
        }

        _mutex.ReleaseMutex();
    }
}
