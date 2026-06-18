using System.Reflection;

namespace TimeTrack.App;

/// <summary>
/// App identity surfaced in the UI — the single source of truth for the version string.
/// The number itself is set once in the project file (&lt;Version&gt; in TimeTrack.App.csproj);
/// bump it for each iteration shipped to a client so they can tell builds apart at a glance.
/// </summary>
internal static class AppInfo
{
    public const string Name = "TimeTrack";

    /// <summary>Display version, e.g. "1.0.0" (any build-metadata suffix stripped).</summary>
    public static string Version { get; } = Resolve();

    /// <summary>Version with a leading "v" for badges/labels, e.g. "v1.0.0".</summary>
    public static string VersionLabel { get; } = "v" + Version;

    private static string Resolve()
    {
        var asm = Assembly.GetExecutingAssembly();

        // InformationalVersion carries the <Version> as authored; the SDK may append
        // "+<git-sha>" build metadata, which we trim for a clean user-facing string.
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            int plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }

        return asm.GetName().Version?.ToString(3) ?? "1.0.0";
    }
}
