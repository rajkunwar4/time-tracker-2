namespace TimeTrack.Core.Security;

/// <summary>The API session token plus the identity it belongs to.</summary>
public sealed class StoredToken
{
    public string Token { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime ObtainedUtc { get; set; }

    /// <summary>
    /// PBKDF2 verifier of the password from the last successful online login, used to
    /// authenticate offline sign-ins. Encoded as "iterations.saltBase64.hashBase64";
    /// never the raw password. Null until the first online login.
    /// </summary>
    public string? PasswordVerifier { get; set; }
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
