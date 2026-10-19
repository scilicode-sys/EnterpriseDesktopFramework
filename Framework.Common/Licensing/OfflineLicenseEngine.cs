using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Framework.Common.Licensing;

/// <summary>
/// Activation hors-ligne par code mathématique (challenge/response) :
/// le client fournit son HWID (défi), le distributeur — seul détenteur de la
/// clé secrète produit — émet un code qui encode la date d'expiration et une
/// signature HMAC-SHA256 de « produit|hwid|expiration ».
/// Blob de code : 4 octets (jours depuis l'époque) + 8 octets de signature
/// = 12 octets = 20 caractères base32 = « AAAAA-BBBBB-CCCCC-DDDDD ».
/// La clé n'est JAMAIS stockée dans le framework : c'est le produit dérivé
/// qui passe la sienne à la composition root.
/// </summary>
public static class OfflineLicenseEngine
{
    private static readonly DateOnly Epoque = new(2020, 1, 1);
    private const int TailleSignature = 8;
    private const int TailleBlob = 4 + TailleSignature;

    /// <summary>Émet le code d'activation pour une machine donnée (rôle distributeur).</summary>
    public static string Emettre(string secretKey, string product, string hwid, DateOnly expirationDate)
    {
        ArgumentException.ThrowIfNullOrEmpty(secretKey);
        ArgumentException.ThrowIfNullOrEmpty(product);
        ArgumentException.ThrowIfNullOrEmpty(hwid);

        var signature = Signer(secretKey, product, hwid, expirationDate);

        var blob = new byte[TailleBlob];
        var jours = expirationDate.DayNumber - Epoque.DayNumber;

        BitConverter.TryWriteBytes(blob.AsSpan(0, 4), jours);
        signature.AsSpan(0, TailleSignature).CopyTo(blob.AsSpan(4));

        return LicenseCodec.FormerPaquets(LicenseCodec.ToBase32(blob));
    }

    /// <summary>
    /// Vérifie un code d'activation : décodage, re-calcul de la signature sur
    /// (produit, hwid, expiration relue), comparaison en temps constant.
    /// </summary>
    public static bool Valider(
        string secretKey,
        string product,
        string hwid,
        string activationCode,
        out DateOnly expirationDate)
    {
        expirationDate = default;

        if (string.IsNullOrEmpty(secretKey) || string.IsNullOrEmpty(activationCode))
        {
            return false;
        }

        byte[] blob;
        try
        {
            blob = LicenseCodec.FromBase32(LicenseCodec.Normaliser(activationCode));
        }
        catch (FormatException)
        {
            return false;
        }

        if (blob.Length != TailleBlob)
        {
            return false;
        }

        var jours = BitConverter.ToInt32(blob, 0);
        if (jours is < 0 or > 30_000)
        {
            return false;
        }

        expirationDate = Epoque.AddDays(jours);
        var attendue = Signer(secretKey, product, hwid, expirationDate);

        return CryptographicOperations.FixedTimeEquals(
            attendue.AsSpan(0, TailleSignature),
            blob.AsSpan(4, TailleSignature));
    }

    private static byte[] Signer(string secretKey, string product, string hwid, DateOnly expirationDate)
    {
        var message = $"{product}|{hwid}|{expirationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
    }
}
