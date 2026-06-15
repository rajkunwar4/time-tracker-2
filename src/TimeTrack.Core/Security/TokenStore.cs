namespace TimeTrack.Core.Security;

/// <summary>The API session token plus the identity it belongs to.</summary>
public sealed class StoredToken
{
    public string Token { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime ObtainedUtc { get; set; }
}

/// <summary>
/// Persists the API token. The Windows implementation encrypts it at rest with
/// DPAPI (per-user). Lives behind an interface so Core stays platform-neutral.
/// </summary>
public interface ITokenStore
{
    void Save(StoredToken token);
    StoredToken? Load();
    void Clear();
}
