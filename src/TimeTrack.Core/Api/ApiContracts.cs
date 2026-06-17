using System.Text.Json.Serialization;

namespace TimeTrack.Core.Api;

// ---- result types returned to callers ----

public sealed class LoginResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string Token { get; init; } = string.Empty;
    public string EmployeeId { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;

    /// <summary>True when the failure was a network/transport error (server not reachable),
    /// as opposed to a reachable server rejecting the credentials. Drives the offline-login fallback.</summary>
    public bool Unreachable { get; init; }

    public static LoginResult Ok(string token, string employeeId, string email, string role) =>
        new() { Success = true, Token = token, EmployeeId = employeeId, Email = email, Role = role };

    public static LoginResult Fail(string error) => new() { Success = false, Error = error };

    public static LoginResult FailUnreachable(string error) =>
        new() { Success = false, Error = error, Unreachable = true };
}

public sealed class IngestResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public int StatusCode { get; init; }
    public IReadOnlyList<Guid> AcceptedIds { get; init; } = Array.Empty<Guid>();

    public static IngestResult Ok(IReadOnlyList<Guid> acceptedIds) =>
        new() { Success = true, AcceptedIds = acceptedIds };

    public static IngestResult Fail(int statusCode, string error) =>
        new() { Success = false, StatusCode = statusCode, Error = error };
}

// ---- wire DTOs (match the Express/Mongo backend) ----

internal sealed class LoginResponse
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("token")] public string? Token { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("user")] public UserDto? User { get; set; }
}

internal sealed class UserDto
{
    [JsonPropertyName("_id")] public string? Id { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("role")] public string? Role { get; set; }
}

internal sealed class ErrorResponse
{
    [JsonPropertyName("message")] public string? Message { get; set; }
}

internal sealed class IntervalDto
{
    [JsonPropertyName("id")] public string id { get; set; } = string.Empty;
    [JsonPropertyName("windowStartUtc")] public DateTime windowStartUtc { get; set; }
    [JsonPropertyName("windowEndUtc")] public DateTime windowEndUtc { get; set; }
    [JsonPropertyName("activeSeconds")] public int activeSeconds { get; set; }
    [JsonPropertyName("clientSentUtc")] public DateTime clientSentUtc { get; set; }
}

internal sealed class IngestResponse
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("data")] public IngestData? Data { get; set; }
}

internal sealed class IngestData
{
    [JsonPropertyName("acceptedIds")] public List<string>? AcceptedIds { get; set; }
    [JsonPropertyName("accepted")] public int Accepted { get; set; }
    [JsonPropertyName("inserted")] public int Inserted { get; set; }
    [JsonPropertyName("duplicates")] public int Duplicates { get; set; }
    [JsonPropertyName("serverTimeUtc")] public string? ServerTimeUtc { get; set; }
}
