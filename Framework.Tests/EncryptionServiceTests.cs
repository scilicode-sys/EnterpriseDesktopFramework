using System.IO;
using System.Security.Cryptography;
using System.Text;
using Framework.Common.Security;
using Xunit;

namespace Framework.Tests;

/// <summary>
/// Chiffrement local : clé AES-GCM par machine sous DPAPI, Protect/Unprotect,
/// détection d'altération. Chaque classe de test utilise un nom d'application
/// unique, détruit à la fin — rien ne reste dans %AppData%.
/// </summary>
public class EncryptionServiceTests : IDisposable
{
    private readonly string _appName;
    private readonly EncryptionService _service;
    private readonly string _repertoire;

    public EncryptionServiceTests()
    {
        _appName = $"FrameworkTests-Crypt-{Guid.NewGuid():N}";
        _service = new EncryptionService(_appName);
        _repertoire = Path.Combine(Path.GetTempPath(), _appName);
        Directory.CreateDirectory(_repertoire);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_repertoire, recursive: true);

            var cle = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), _appName);
            if (Directory.Exists(cle))
            {
                Directory.Delete(cle, recursive: true);
            }
        }
        catch (IOException)
        {
            // Un reste temporaire de test ne juge pas la suite.
        }
    }

    [Fact]
    public void Cles_StablesEntreAppels_EtIndependantesParMachine()
    {
        var premiere = _service.GetKey();
        var seconde = _service.GetKey();

        Assert.Equal(32, premiere.Length);
        Assert.Equal(premiere, seconde);
    }

    [Fact]
    public void Fichier_ChiffrePuisDechiffre_RendLIdentique()
    {
        var source = EcrireSource("Données sensibles été 2026, accents compris éàû.");
        var chiffre = Path.Combine(_repertoire, "secret.fwk");
        var rendu = Path.Combine(_repertoire, "retour.txt");

        _service.EncryptFile(source, chiffre);
        _service.DecryptFile(chiffre, rendu);

        Assert.Equal(File.ReadAllText(source), File.ReadAllText(rendu));
    }

    [Fact]
    public void Fichier_Chiffre_VraimentDifferentDuClair()
    {
        var source = EcrireSource("MARKER-INTERNE-NE-DOIT-PARAITRE");
        var chiffre = Path.Combine(_repertoire, "secret.fwk");

        _service.EncryptFile(source, chiffre);

        var brut = File.ReadAllText(chiffre, Encoding.Latin1);
        Assert.DoesNotContain("MARKER-INTERNE", brut);
        Assert.StartsWith("FWK1", brut);
    }

    [Fact]
    public void Fichier_Tronque_DechiffrementLeve()
    {
        var source = EcrireSource("contenu correct");
        var chiffre = Path.Combine(_repertoire, "secret.fwk");

        _service.EncryptFile(source, chiffre);
        File.WriteAllBytes(chiffre, File.ReadAllBytes(chiffre)[..5]);

        Assert.Throws<CryptographicException>(() =>
            _service.DecryptFile(chiffre, Path.Combine(_repertoire, "x.txt")));
    }

    [Fact]
    public void Fichier_BitAltered_DechiffrementLeve()
    {
        var source = EcrireSource(new string('A', 2000));
        var chiffre = Path.Combine(_repertoire, "secret.fwk");

        _service.EncryptFile(source, chiffre);

        var octets = File.ReadAllBytes(chiffre);
        octets[^20] ^= 0xFF;
        File.WriteAllBytes(chiffre, octets);

        Assert.ThrowsAny<CryptographicException>(() =>
            _service.DecryptFile(chiffre, Path.Combine(_repertoire, "x.txt")));
    }

    [Fact]
    public void Fichier_GrosVolume_PasseLaTailleDeBloc()
    {
        var source = EcrireSource(new string('z', (1 << 20) + 12345));
        var chiffre = Path.Combine(_repertoire, "gros.fwk");
        var rendu = Path.Combine(_repertoire, "gros.txt");

        _service.EncryptFile(source, chiffre);
        _service.DecryptFile(chiffre, rendu);

        Assert.Equal(File.ReadAllText(source), File.ReadAllText(rendu));
    }

    [Fact]
    public void Protect_Unprotect_BlobEnMemoire_RondComplet()
    {
        var secret = Encoding.UTF8.GetBytes("jeton-de-session-42");

        var protege = _service.Protect(secret);
        var rendu = _service.Unprotect(protege);

        Assert.Equal(secret, rendu);
        Assert.NotEqual(secret, protege);
    }

    private string EcrireSource(string contenu)
    {
        var chemin = Path.Combine(_repertoire, $"{Guid.NewGuid():N}.txt");
        File.WriteAllText(chemin, contenu);
        return chemin;
    }
}
