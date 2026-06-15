using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TimeTrack.Core.Security;

namespace TimeTrack.App.Services;

/// <summary>
/// Persists the API token encrypted at rest with Windows DPAPI (current-user scope),
/// so only the logged-in Windows user can decrypt it.
/// </summary>
internal sealed class DpapiTokenStore : ITokenStore
{
    private readonly string _path;

    public DpapiTokenStore(string path) => _path = path;

    public void Save(StoredToken token)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(token);
        var encrypted = ProtectedData.Protect(json, optionalEntropy: null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllBytes(_path, encrypted);
    }

    public StoredToken? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var encrypted = File.ReadAllBytes(_path);
            var json = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<StoredToken>(json);
        }
        catch
        {
            return null; // corrupt / unreadable → treat as no token
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        catch
        {
            // ignore
        }
    }
}
