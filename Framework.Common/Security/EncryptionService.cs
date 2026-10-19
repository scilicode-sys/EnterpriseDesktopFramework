using System.IO;
using System.Security.Cryptography;

namespace Framework.Common.Security;

/// <summary>
/// Chiffrement local : une clé AES-256 **aléatoire par machine**, stockée
/// protégée par DPAPI (scope CurrentUser) sous %AppData%\{AppName}\.
/// Format de fichier : « FWK1 » + nonce 12 octets + blocs (longueur 32 bits LE
/// + chiffré + tag 16 octets), AAD de bloc = nonce + index.
/// </summary>
public sealed class EncryptionService
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int BlocSize = 1 << 20;
    private static readonly byte[] Magic = "FWK1"u8.ToArray();
    private static readonly byte[] Entropie = "framework.machine.aes.v1"u8.ToArray();

    private readonly string _cheminCle;
    private byte[]? _cleEnMemoire;

    public EncryptionService(string appName)
    {
        var repertoire = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName);

        Directory.CreateDirectory(repertoire);
        _cheminCle = Path.Combine(repertoire, "machine.key");
    }

    /// <summary>Clé de la machine : créée (et protégée) au premier usage, jamais régénérée en silence.</summary>
    public byte[] GetKey()
    {
        if (_cleEnMemoire is not null)
        {
            return _cleEnMemoire;
        }

        if (File.Exists(_cheminCle))
        {
            var protège = File.ReadAllBytes(_cheminCle);
            _cleEnMemoire = ProtectedData.Unprotect(protège, Entropie, DataProtectionScope.CurrentUser);
            return _cleEnMemoire;
        }

        var cle = RandomNumberGenerator.GetBytes(KeySize);
        var protection = ProtectedData.Protect(cle, Entropie, DataProtectionScope.CurrentUser);

        var temporaire = _cheminCle + ".tmp";
        File.WriteAllBytes(temporaire, protection);
        File.Move(temporaire, _cheminCle, overwrite: true);

        _cleEnMemoire = cle;
        return cle;
    }

    /// <summary>Chiffre un fichier source dans un fichier destination (opération atomique via fichier temporaire).</summary>
    public void EncryptFile(string sourcePath, string destinationPath)
    {
        var cle = GetKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var temporaire = destinationPath + ".enc.tmp";

        using (var entree = File.OpenRead(sourcePath))
        using (var sortie = File.Create(temporaire))
        {
            sortie.Write(Magic);
            sortie.Write(nonce);

            using var aes = new AesGcm(cle, TagSize);

            var index = 0u;
            var tamponClair = new byte[BlocSize];
            int lus;

            while ((lus = entree.Read(tamponClair)) > 0)
            {
                EcrireBloc(aes, nonce, index, sortie, tamponClair, lus);
                index++;
            }
        }

        File.Move(temporaire, destinationPath, overwrite: true);
    }

    /// <summary>Déchiffre un fichier produit par EncryptFile. Lève si l'en-tête ou un tag est corrompu.</summary>
    public void DecryptFile(string sourcePath, string destinationPath)
    {
        var cle = GetKey();
        var temporaire = destinationPath + ".dec.tmp";

        using (var entree = File.OpenRead(sourcePath))
        using (var sortie = File.Create(temporaire))
        {
            var entete = new byte[Magic.Length + NonceSize];
            if (entree.Read(entete) != entete.Length)
            {
                throw new CryptographicException("Fichier chiffré tronqué.");
            }

            if (!entete.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            {
                throw new CryptographicException("Format de fichier chiffré non reconnu.");
            }

            var nonce = entete.AsSpan(Magic.Length).ToArray();

            using var aes = new AesGcm(cle, TagSize);

            var index = 0u;
            var longueurBrute = new byte[4];
            var tamponChiffre = new byte[BlocSize + TagSize];

            while (true)
            {
                if (entree.Read(longueurBrute) == 0)
                {
                    break;
                }

                var tailleBloc = BitConverter.ToInt32(longueurBrute);
                var totalLu = 0;

                while (totalLu < tailleBloc)
                {
                    var lu = entree.Read(tamponChiffre.AsSpan(totalLu, tailleBloc - totalLu));
                    if (lu == 0)
                    {
                        throw new CryptographicException("Fichier chiffré tronqué en cours de lecture.");
                    }

                    totalLu += lu;
                }

                LireBloc(aes, nonce, index, sortie, tamponChiffre, tailleBloc);
                index++;
            }
        }

        File.Move(temporaire, destinationPath, overwrite: true);
    }

    private static void EcrireBloc(AesGcm aes, byte[] nonce, uint index, Stream sortie, byte[] clair, int longueur)
    {
        var aad = Aad(nonce, index);
        var tag = new byte[TagSize];
        var chiffe = new byte[longueur];

        aes.Encrypt(aad, nonce, clair.AsSpan(0, longueur), chiffe, tag);

        sortie.Write(BitConverter.GetBytes(longueur + TagSize));
        sortie.Write(chiffe);
        sortie.Write(tag);
    }

    private static void LireBloc(AesGcm aes, byte[] nonce, uint index, Stream sortie, byte[] bloc, int tailleBloc)
    {
        var aad = Aad(nonce, index);
        var clair = new byte[tailleBloc - TagSize];
        var tag = bloc.AsSpan(tailleBloc - TagSize, TagSize).ToArray();

        aes.Decrypt(aad, nonce, bloc.AsSpan(0, tailleBloc - TagSize), tag, clair);
        sortie.Write(clair);
    }

    private static byte[] Aad(byte[] nonce, uint index)
    {
        var aad = new byte[NonceSize + 4];
        nonce.CopyTo(aad, 0);
        BitConverter.TryWriteBytes(aad.AsSpan(NonceSize), index);
        return aad;
    }

    /// <summary>Protège un petit blob en mémoire (DPAPI CurrentUser) — secrets de session, jetons.</summary>
    public byte[] Protect(byte[] data)
        => ProtectedData.Protect(data, Entropie, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] protectedData)
        => ProtectedData.Unprotect(protectedData, Entropie, DataProtectionScope.CurrentUser);
}
