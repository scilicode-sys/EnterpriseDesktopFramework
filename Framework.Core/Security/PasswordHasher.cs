using System.Security.Cryptography;

namespace Framework.Core.Security;

/// <summary>
/// Hachage de mot de passe PBKDF2-SHA256, format auto-descriptif
/// « pbkdf2$sha256$&lt;iterations&gt;$&lt;sel&gt;$&lt;empreinte&gt; », comparaison en temps constant.
/// Aucune dépendance NuGet : tout vient de System.Security.Cryptography.
/// </summary>
public static class PasswordHasher
{
    private const int Iterations = 150_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);

        return string.Join('$', "pbkdf2", "sha256", Iterations.ToString(), Convert.ToBase64String(salt), Convert.ToBase64String(key));
    }

    public static bool Verify(string? password, string? storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        var parts = storedHash.Split('$');
        if (parts.Length != 5 || parts[0] != "pbkdf2" || parts[1] != "sha256")
        {
            return false;
        }

        if (!int.TryParse(parts[2], out var iterations) || iterations < 10_000 || iterations > 2_000_000)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
