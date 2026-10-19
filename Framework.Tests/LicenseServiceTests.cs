using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Framework.Common.Licensing;
using Framework.Core.Licensing;
using Framework.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Framework.Tests;

/// <summary>Réglages en mémoire pour tests — le disque n'est pas nécessaire ici.</summary>
internal sealed class ReglagesEnMemoire : ISettingsService
{
    private readonly Dictionary<string, object?> _valeurs = new(StringComparer.Ordinal);

    public T Get<T>(string key, T defaultValue)
        => _valeurs.TryGetValue(key, out var valeur) && valeur is T tape ? tape : defaultValue;

    public void Set<T>(string key, T value) => _valeurs[key] = value;

    public bool TryGet<T>(string key, out T? value)
    {
        if (_valeurs.TryGetValue(key, out var brute) && brute is T tape)
        {
            value = tape;
            return true;
        }

        value = default;
        return false;
    }

    public bool Has(string key) => _valeurs.ContainsKey(key);

    public void Delete(string key) => _valeurs.Remove(key);
}

/// <summary>HwidProvider : format lisible garanti et stabilité sur la machine.</summary>
public class HwidProviderTests
{
    [Fact]
    public void Get_FormatHexadécimalGroupé()
    {
        var hwid = new HwidProvider().Get();

        Assert.Matches(@"^[0-9A-F]{8}(-[0-9A-F]{8}){3}$", hwid);
    }

    [Fact]
    public void Get_StableEntreInstances()
    {
        Assert.Equal(new HwidProvider().Get(), new HwidProvider().Get());
    }
}

/// <summary>Contrôle en ligne : seule une réponse explicite « revoked » punit le poste.</summary>
public class OnlineLicenseValidatorTests
{
    private static OnlineLicenseValidator Createurs(Func<HttpRequestMessage, HttpResponseMessage> reponse)
        => new(new HttpClient(new HandlersStub(reponse)), NullLogger.Instance);

    [Fact]
    public async Task Vérifier_ServeurInjoignable_Unreachable()
    {
        var validator = new OnlineLicenseValidator(new HttpClient(), NullLogger.Instance);

        var resultat = await validator.VérifierAsync("http://127.0.0.1:1", "Produit", "HWID");

        Assert.Equal(OnlineCheckResult.Unreachable, resultat);
    }

    [Fact]
    public async Task Vérifier_RéponseActive_Active()
    {
        var validator = Createurs(_ => Reponse("""{"status":"active"}"""));

        Assert.Equal(OnlineCheckResult.Active, await validator.VérifierAsync("https://x", "P", "H"));
    }

    [Fact]
    public async Task Vérifier_RéponseRevoked_Revoked()
    {
        var validator = Createurs(_ => Reponse("""{"status":"revoked"}"""));

        Assert.Equal(OnlineCheckResult.Revoked, await validator.VérifierAsync("https://x", "P", "H"));
    }

    [Fact]
    public async Task Vérifier_404_UnreachableJamaisRevoked()
    {
        var validator = Createurs(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Equal(OnlineCheckResult.Unreachable, await validator.VérifierAsync("https://x", "P", "H"));
    }

    private static HttpResponseMessage Reponse(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class HandlersStub : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _fabrique;

        public HandlersStub(Func<HttpRequestMessage, HttpResponseMessage> fabrique) => _fabrique = fabrique;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_fabrique(request));
    }
}

/// <summary>Licence du poste : cycle complet activation → expiration → retrait.</summary>
public class LicenseServiceTests : IDisposable
{
    private const string Cle = "cle-de-test-produit";

    private readonly string _repertoire;
    private readonly HwidProvider _hwid = new();
    private readonly FakeHorloge _horloge = new();
    private readonly LicenseService _licence;

    public LicenseServiceTests()
    {
        _repertoire = Path.Combine(Path.GetTempPath(), $"FrameworkTests-Licence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_repertoire);

        _licence = new LicenseService(
            _hwid,
            _horloge,
            new ReglagesEnMemoire(),
            NullLogger<LicenseService>.Instance,
            "ProduitTest",
            Cle,
            licenceDirectory: _repertoire);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_repertoire, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void PosteNeuf_StatutMissing()
    {
        Assert.Equal(LicenseStatus.Missing, _licence.GetStatus(out var info));
        Assert.Null(info);
    }

    [Fact]
    public void ActivationValide_PuisStatutValid_AvecInfos()
    {
        var expiration = DateOnly.FromDateTime(_horloge.Maintenant).AddYears(1);
        var code = OfflineLicenseEngine.Emettre(Cle, "ProduitTest", _licence.GetChallenge(), expiration);

        var resultat = _licence.Activate(code, "Client Été");

        Assert.True(resultat.IsSuccess);
        Assert.Equal(LicenseStatus.Valid, _licence.GetStatus(out var info));
        Assert.NotNull(info);
        Assert.Equal("Client Été", info!.Customer);
        Assert.Equal(expiration, info.ExpirationDate);
        Assert.Equal(_licence.GetChallenge(), info.Hwid);
    }

    [Fact]
    public void Activation_MauvaisCode_Refusée()
    {
        var resultat = _licence.Activate("AAAAA-BBBBB-CCCCC-DDDDD", "Client");

        Assert.Equal(LicenseStatus.Invalid, resultat.Status);
        Assert.Equal(LicenseStatus.Missing, _licence.GetStatus(out _));
    }

    [Fact]
    public void LicenceValide_PuisHorlogeAvancée_Expiree()
    {
        var expiration = DateOnly.FromDateTime(_horloge.Maintenant).AddDays(2);
        var code = OfflineLicenseEngine.Emettre(Cle, "ProduitTest", _licence.GetChallenge(), expiration);
        _licence.Activate(code, "Client");

        _horloge.Maintenant = expiration.ToDateTime(new TimeOnly(12, 0)).AddDays(1);

        Assert.Equal(LicenseStatus.Expired, _licence.GetStatus(out var info));
        Assert.NotNull(info);
    }

    [Fact]
    public void LicenceDeLAnotherPoste_HardwareMismatch()
    {
        var autreHwid = "01234567-89ABCDEF-01234567-89ABCDEF";
        var expiration = DateOnly.FromDateTime(_horloge.Maintenant).AddYears(1);
        var code = OfflineLicenseEngine.Emettre(Cle, "ProduitTest", autreHwid, expiration);

        // Le fichier est écrit tel quel par l'éditeur : il ne lie PAS ce poste.
        File.WriteAllText(
            Path.Combine(_repertoire, "licence.json"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                Product = "ProduitTest",
                Customer = "Quelqu'un d'autre",
                Hwid = autreHwid,
                Expiration = expiration.ToString("yyyy-MM-dd"),
                Code = LicenseCodec.Normaliser(code),
            }));

        Assert.Equal(LicenseStatus.HardwareMismatch, _licence.GetStatus(out _));
    }

    [Fact]
    public void FichierCorrompu_StatutInvalid()
    {
        File.WriteAllText(Path.Combine(_repertoire, "licence.json"), "{ pas du tout licence }");

        Assert.Equal(LicenseStatus.Invalid, _licence.GetStatus(out _));
    }

    [Fact]
    public void Retrait_PuisMissingEtNouvelleActivationPossible()
    {
        var expiration = DateOnly.FromDateTime(_horloge.Maintenant).AddYears(1);
        var code = OfflineLicenseEngine.Emettre(Cle, "ProduitTest", _licence.GetChallenge(), expiration);
        _licence.Activate(code, "Client");

        _licence.Remove();
        Assert.Equal(LicenseStatus.Missing, _licence.GetStatus(out _));

        Assert.True(_licence.Activate(code, "Client").IsSuccess);
    }

    [Fact]
    public async Task ContrôleEnLigne_SansUrlConfigurée_Actif()
    {
        Assert.Equal(OnlineCheckResult.Active, await _licence.CheckOnlineAsync());
    }
}
