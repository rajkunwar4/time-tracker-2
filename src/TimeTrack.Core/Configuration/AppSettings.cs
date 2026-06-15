using System.Text.Json;

namespace TimeTrack.Core.Configuration;

/// <summary>Strongly-typed app configuration, loaded from appsettings.json beside the exe.</summary>
public sealed class AppSettings
{
    public ApiSettings Api { get; set; } = new();
    public TrackingSettings Tracking { get; set; } = new();

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Load from the given path; returns defaults if the file is missing or invalid.</summary>
    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
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
    /// <summary>Base URL of the Equicom API, including the /api/v1 prefix.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:4000/api/v1";

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
