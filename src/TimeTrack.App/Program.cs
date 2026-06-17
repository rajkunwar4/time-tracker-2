using System.Windows.Forms;
using TimeTrack.App.Services;
using TimeTrack.Core.Api;
using TimeTrack.Core.Configuration;
using TimeTrack.Core.Storage;

namespace TimeTrack.App;

internal static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    private static void Main()
    {
        // Single instance — only one tracker per machine session.
        _mutex = new Mutex(initiallyOwned: true, "TimeTrack_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("TimeTrack is already running.", "TimeTrack",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

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

        var api = new TimeTrackApiClient(settings.Api.BaseUrl, timeoutSeconds: settings.Api.TimeoutSeconds);
        var tokenStore = new DpapiTokenStore(Path.Combine(dataDir, "token.bin"));

        // The context owns the always-on lifecycle: login ⇄ main, minimize-to-tray.
        Application.Run(new TrackerAppContext(settings, outbox, api, tokenStore));

        _mutex.ReleaseMutex();
    }
}
