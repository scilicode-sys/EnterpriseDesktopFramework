using System.Text;

namespace Framework.Common.Licensing;

/// <summary>
/// Encodage base32 « Crockford » sans padding : alphabet sans I, L, O, U,
/// tolérant à la recopie au téléphone ou sur papier. Les codes d'activation
/// sont rendus en paquets de 5 caractères séparés par des tirets.
/// </summary>
public static class LicenseCodec
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int TaillePaquet = 5;

    public static string ToBase32(byte[] data)
    {
        var rendu = new StringBuilder(data.Length * 8 / 5 + 1);
        var tampon = 0;
        var bits = 0;

        foreach (var octet in data)
        {
            tampon = (tampon << 8) | octet;
            bits += 8;

            while (bits >= 5)
            {
                bits -= 5;
                rendu.Append(Alphabet[(tampon >> bits) & 0x1F]);
            }
        }

        if (bits > 0)
        {
            rendu.Append(Alphabet[(tampon << (5 - bits)) & 0x1F]);
        }

        return rendu.ToString();
    }

    public static byte[] FromBase32(string encoded)
    {
        ArgumentException.ThrowIfNullOrEmpty(encoded);

        var tampon = 0;
        var bits = 0;
        var rendu = new List<byte>(encoded.Length * 5 / 8 + 1);

        foreach (var c in encoded)
        {
            var valeur = Alphabet.IndexOf(char.ToUpperInvariant(c));
            if (valeur < 0)
            {
                throw new FormatException($"Caractère hors alphabet base32 : « {c} ».");
            }

            tampon = (tampon << 5) | valeur;
            bits += 5;

            if (bits >= 8)
            {
                bits -= 8;
                rendu.Add((byte)((tampon >> bits) & 0xFF));
            }
        }

        return rendu.ToArray();
    }

    /// <summary>Rend un flux base32 en paquets de 5 : « AAAAA-BBBBB-… ».</summary>
    public static string FormerPaquets(string base32)
    {
        ArgumentException.ThrowIfNullOrEmpty(base32);

        var rendu = new StringBuilder(base32.Length + base32.Length / TaillePaquet);

        for (var i = 0; i < base32.Length; i++)
        {
            if (i > 0 && i % TaillePaquet == 0)
            {
                rendu.Append('-');
            }

            rendu.Append(base32[i]);
        }

        return rendu.ToString();
    }

    /// <summary>Élimine tirets, espaces et retours avant décodage (saisie humaine tolérée).</summary>
    public static string Normaliser(string code)
        => new(code.Where(c => c != '-' && !char.IsWhiteSpace(c)).ToArray());
}
