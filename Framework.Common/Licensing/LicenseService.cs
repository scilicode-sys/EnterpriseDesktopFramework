using System.IO;
using System.Net.Http;
using Framework.Core.Licensing;
using Framework.Core.Settings;
using Framework.Core.Time;
using Microsoft.Extensions.Logging;

namespace Framework.Common.Licensing;

/// <summary>
/// Licence du poste, conforme au contrat Core : activation hors-ligne par code
/// mathématique lié au HWID (OfflineLicenseEngine), fichier JSON atomique sous
/// %ProgramData%\{AppName}\, contrôle de révocation en ligne optionnel.
/// La clé secrète produit est injectée par le projet dérivé — jamais en dur ici.
/// </summary>
public sealed class LicenseService : ILicenseService
{
    public const string OnlineUrlSettingsKey = "License.OnlineUrl";

    private readonly HwidProvider _hwid;
    private readonly IHorloge _horloge;
    private readonly ISettingsService _settings;
    private readonly OnlineLicenseValidator _online;
    private readonly ILogger<LicenseService> _logger;
    private readonly string _secretKey;
    private readonly string _cheminFichier;

    /// <summary>Code produit — l'empreinte à laquelle les codes d'activation sont liés.</summary>
    public string Product { get; }

    public LicenseService(
        HwidProvider hwid,
        IHorloge horloge,
        ISettingsService settings,
        ILogger<LicenseService> logger,
        string appName,
        string secretKey,
        HttpClient? httpClient = null,
        string? licenceDirectory = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(appName);
        ArgumentException.ThrowIfNullOrEmpty(secretKey);

        _hwid = hwid;
        _horloge = horloge;
        _settings = settings;
        _logger = logger;
        _secretKey = secretKey;
        Product = appName;

        var repertoire = licenceDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), appName);

        Directory.CreateDirectory(repertoire);
        _cheminFichier = Path.Combine(repertoire, "licence.json");

        _online = new OnlineLicenseValidator(httpClient ?? new HttpClient(), logger);
    }

    public string GetChallenge() => _hwid.Get();

    public LicenseResult Activate(string activationCode, string customer)
    {
        var hwid = _hwid.Get();

        if (!OfflineLicenseEngine.Valider(_secretKey, Product, hwid, activationCode, out var expiration))
        {
            _logger.LogWarning("Activation refusée : code non valable pour ce poste ({Hwid}).", hwid);
            return new LicenseResult(LicenseStatus.Invalid,
                "Code d'activation non valable pour ce poste. Vérifiez la recopie, groupement par groupement.");
        }

        var dossier = new DossierLicence
        {
            Product = Product,
            Customer = customer,
            Hwid = hwid,
            Expiration = expiration.ToString("yyyy-MM-dd"),
            Code = LicenseCodec.Normaliser(activationCode),
        };

        EcrireFichier(dossier);
        _logger.LogInformation("Licence activée : « {Client} » jusqu'au {Expiration:yyyy-MM-dd}.", customer, expiration.ToDateTime(new TimeOnly(23, 59)));

        return LicenseResult.Ok($"Licence enregistrée pour « {customer} » jusqu'au {expiration:yyyy-MM-dd}.");
    }

    public LicenseStatus GetStatus(out LicenseInfo? info)
    {
        info = null;

        var dossier = LireFichier();
        if (dossier is null)
        {
            return File.Exists(_cheminFichier) ? LicenseStatus.Invalid : LicenseStatus.Missing;
        }

        var hwid = _hwid.Get();

        if (!DateOnly.TryParseExact(dossier.Expiration, "yyyy-MM-dd", out var expiration))
        {
            return LicenseStatus.Invalid;
        }

        var signatureValide = OfflineLicenseEngine.Valider(_secretKey, Product, hwid, dossier.Code, out _);

        if (!signatureValide)
        {
            var statut = dossier.Hwid != hwid ? LicenseStatus.HardwareMismatch : LicenseStatus.Invalid;

            _logger.LogWarning("Licence non valable sur ce poste : {Statut}.", statut);
            return statut;
        }

        info = new LicenseInfo(dossier.Product, dossier.Customer, expiration, dossier.Hwid);

        if (expiration.ToDateTime(new TimeOnly(23, 59)) < _horloge.Maintenant)
        {
            return LicenseStatus.Expired;
        }

        return LicenseStatus.Valid;
    }

    public async Task<OnlineCheckResult> CheckOnlineAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.TryGet<string>(OnlineUrlSettingsKey, out var url) || string.IsNullOrEmpty(url))
        {
            // Aucun feed de révocation configuré : le poste n'est pas puni pour un
            // choix du distributeur — la licence hors-ligne reste la référence.
            return OnlineCheckResult.Active;
        }

        return await _online.VérifierAsync(url!, Product, _hwid.Get(), cancellationToken);
    }

    public void Remove()
    {
        if (File.Exists(_cheminFichier))
        {
            File.Delete(_cheminFichier);
            _logger.LogInformation("Licence retirée de ce poste.");
        }
    }

    private DossierLicence? LireFichier()
    {
        try
        {
            if (!File.Exists(_cheminFichier))
            {
                return null;
            }

            return System.Text.Json.JsonSerializer.Deserialize<DossierLicence>(File.ReadAllText(_cheminFichier));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Fichier de licence illisible : poste traité sans licence.");
            return null;
        }
    }

    private void EcrireFichier(DossierLicence dossier)
    {
        var temporaire = _cheminFichier + ".tmp";
        File.WriteAllText(temporaire, System.Text.Json.JsonSerializer.Serialize(dossier));
        File.Move(temporaire, _cheminFichier, overwrite: true);
    }

    /// <summary>Forme persistée du fichier licence (interne au service).</summary>
    private sealed class DossierLicence
    {
        public string Product { get; set; } = "";
        public string Customer { get; set; } = "";
        public string Hwid { get; set; } = "";
        public string Expiration { get; set; } = "";
        public string Code { get; set; } = "";
    }
}
