using System.Text.Json;

namespace TimeTrack.Core.Configuration;

/// <summary>Strongly-typed app configuration, loaded from appsettings.json beside the exe.</summary>
public sealed class AppSettings
{
    public ApiSettings Api { get; set; } = new();
    public TrackingSettings Tracking { get; set; } = new();
    public StartupSettings Startup { get; set; } = new();
    public ProxySettings Proxy { get; set; } = new();

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Load configuration. A sibling <c>appsettings.Local.json</c>, if present, takes precedence
    /// over <paramref name="path"/> — this is the gitignored developer override (e.g. pointing at a
    /// local backend) and is never shipped, so released clients always use the production
    /// <c>appsettings.json</c>. Missing/invalid files fall back to the built-in defaults below.
    /// </summary>
    public static AppSettings Load(string path)
    {
        var dir = Path.GetDirectoryName(path);
        var localPath = dir is null ? "appsettings.Local.json" : Path.Combine(dir, "appsettings.Local.json");
        var effectivePath = File.Exists(localPath) ? localPath : path;
        try
        {
            if (File.Exists(effectivePath))
            {
                var json = File.ReadAllText(effectivePath);
                return JsonSerializer.Deserialize<AppSettings>(json, Opts) ?? new AppSettings();
            }
        }
        catch
        {
            // fall through to defaults
        }
        return new AppSettings();
    }
}

public sealed class ApiSettings
{
    /// <summary>
    /// Base URL of the Equicom API, including the /api/v1 prefix. Default is the production
    /// cloud API; override per deployment in appsettings.json (or appsettings.Local.json for dev).
    /// </summary>
    public string BaseUrl { get; set; } = "https://backend.software.equicom.co/api/v1";

    /// <summary>HTTP timeout for API calls, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 15;
}

public sealed class TrackingSettings
{
    /// <summary>No input for this long (seconds) flips the working state to idle.</summary>
    public int IdleThresholdSeconds { get; set; } = 300;

    /// <summary>How many seconds of tracking accumulate before an interval is queued.</summary>
    public int WindowSeconds { get; set; } = 60;
}

public sealed class StartupSettings
{
    /// <summary>
    /// Register the app to launch automatically at Windows sign-in (per-user, non-admin).
    /// Set to false on a development machine to stop the build auto-launching each logon.
    /// </summary>
    public bool RunAtLogon { get; set; } = true;
}

public sealed class ProxySettings
{
    /// <summary>
    /// When true, the app opens a time-boxed internet window for syncing by toggling the
    /// Windows system proxy to <see cref="Address"/>:<see cref="Port"/>, and routes its own
    /// API traffic through that proxy. Debug builds ignore this (always direct) so a dev
    /// machine's browsing isn't disrupted.
    ///
    /// <para>Default is <b>false</b> (talk to the cloud directly) — correct for testing and any
    /// environment with normal internet. Set it to <b>true</b> only on the locked-down client PCs
    /// that have no continuous internet and must reach the cloud via the master proxy.</para>
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>The master PC / gateway proxy address (same on every client).</summary>
    public string Address { get; set; } = "192.168.137.1";

    /// <summary>The proxy port.</summary>
    public int Port { get; set; } = 808;

    /// <summary>Failsafe: auto-close the window (clear the proxy) after this many minutes.</summary>
    public int MaxWindowMinutes { get; set; } = 15;
}
