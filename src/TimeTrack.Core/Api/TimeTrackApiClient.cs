using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TimeTrack.Core.Models;

namespace TimeTrack.Core.Api;

/// <summary>
/// Typed client for the Equicom API: login (JWT) and the idempotent interval ingest.
/// </summary>
public sealed class TimeTrackApiClient
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public TimeTrackApiClient(string baseUrl, HttpClient? http = null, int timeoutSeconds = 15)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
    }

    /// <summary>POST /auth/login → JWT + identity.</summary>
    public async Task<LoginResult> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { email, password });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync($"{_baseUrl}/auth/login", content, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                return LoginResult.Fail(ExtractMessage(body) ?? $"Login failed ({(int)resp.StatusCode})");

            var parsed = JsonSerializer.Deserialize<LoginResponse>(body, Json);
            if (parsed?.Token is null || parsed.User?.Id is null)
                return LoginResult.Fail("Login response was missing a token");

            return LoginResult.Ok(parsed.Token, parsed.User.Id, parsed.User.Email ?? email, parsed.User.Role ?? "");
        }
        catch (Exception ex)
        {
            return LoginResult.Fail($"Could not reach the server: {ex.Message}");
        }
    }

    /// <summary>POST /timeTracking/intervals (Bearer) → accepted interval ids.</summary>
    public async Task<IngestResult> PostIntervalsAsync(string token, IReadOnlyList<IntervalRecord> records, CancellationToken ct = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            var dto = records.Select(r => new IntervalDto
            {
                id = r.Id.ToString(),
                windowStartUtc = r.WindowStartUtc,
                windowEndUtc = r.WindowEndUtc,
                activeSeconds = r.ActiveSeconds,
                clientSentUtc = now
            }).ToList();

            var payload = JsonSerializer.Serialize(dto, Json);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/timeTracking/intervals");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                return IngestResult.Fail((int)resp.StatusCode, ExtractMessage(body) ?? body);

            var parsed = JsonSerializer.Deserialize<IngestResponse>(body, Json);
            var accepted = (parsed?.Data?.AcceptedIds ?? new List<string>())
                .Select(s => Guid.TryParse(s, out var g) ? g : (Guid?)null)
                .Where(g => g.HasValue)
                .Select(g => g!.Value)
                .ToList();

            return IngestResult.Ok(accepted);
        }
        catch (Exception ex)
        {
            return IngestResult.Fail(0, ex.Message);
        }
    }

    private static string? ExtractMessage(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<ErrorResponse>(body, Json)?.Message;
        }
        catch
        {
            return null;
        }
    }
}
