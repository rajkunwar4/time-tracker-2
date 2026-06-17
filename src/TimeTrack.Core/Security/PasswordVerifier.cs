using System.Security.Cryptography;

namespace TimeTrack.Core.Security;

/// <summary>
/// Creates and checks a PBKDF2 password verifier for offline login. We store a salted,
/// iterated hash — never the raw password — so a cached credential can authenticate an
/// offline sign-in without the password ever being recoverable from disk.
///
/// <para>Encoded as <c>iterations.saltBase64.hashBase64</c>. The whole thing is additionally
/// encrypted at rest by the DPAPI token store.</para>
/// </summary>
public static class PasswordVerifier
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    /// <summary>Produce a verifier string for the given password.</summary>
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashSize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    /// <summary>Check a password against a previously-stored verifier (constant-time).</summary>
    public static bool Verify(string password, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;

        var parts = stored.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations) || iterations <= 0)
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expected = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
