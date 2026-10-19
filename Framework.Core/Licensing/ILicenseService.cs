namespace Framework.Core.Licensing;

/// <summary>
/// État d'une licence sur ce poste, rendu par ILicenseService.Etat().
/// </summary>
public enum LicenseStatus
{
    /// <summary>Licence présente, signée correctement, liée à ce matériel et non expirée.</summary>
    Valid,

    /// <summary>Aucun fichier de licence sur ce poste.</summary>
    Missing,

    /// <summary>La date d'expiration est passée (au sens de IHorloge).</summary>
    Expired,

    /// <summary>La licence existe mais n'est pas liée à l'identifiant matériel de ce poste.</summary>
    HardwareMismatch,

    /// <summary>Fichier illisible, code altéré ou signature HMAC invalide.</summary>
    Invalid,

    /// <summary>Le service en ligne a répondu que la licence est révoquée.</summary>
    OnlineRevoked,
}

/// <summary>Une licence en ligne ne répond jamais : on ne punit pas le poste pour une panne réseau.</summary>
public enum OnlineCheckResult
{
    Active,
    Revoked,
    Unreachable,
}

/// <summary>Informations lues dans le fichier de licence local.</summary>
public sealed record LicenseInfo(
    string Product,
    string Customer,
    DateOnly ExpirationDate,
    string Hwid);

/// <summary>Résultat d'une activation : statut + message français prêt à l'écran.</summary>
public sealed record LicenseResult(LicenseStatus Status, string? Message = null)
{
    public bool IsSuccess => Status == LicenseStatus.Valid;

    public static LicenseResult Ok(string message) => new(LicenseStatus.Valid, message);
}

/// <summary>
/// Contrat de licence du poste : défi matériel, activation hors-ligne par
/// code mathématique (challenge/response), état courant, contrôle en ligne optionnel.
/// Aucune dépendance : l'implémentation vit dans Framework.Common.
/// </summary>
public interface ILicenseService
{
    /// <summary>Identifiant matériel (HWID) de ce poste — le défi à transmettre au distributeur.</summary>
    string GetChallenge();

    /// <summary>Active la machine avec le code de réponse fourni par le distributeur.</summary>
    LicenseResult Activate(string activationCode, string customer);

    /// <summary>Licence courante : fichier + HWID + signature + date vérifiés à la volée.</summary>
    LicenseStatus GetStatus(out LicenseInfo? info);

    /// <summary>Contrôle en ligne (révocation) ; ne lève jamais en cas de panne réseau.</summary>
    Task<OnlineCheckResult> CheckOnlineAsync(CancellationToken cancellationToken = default);

    /// <summary>Retire la licence locale (désinstallation, changement de poste).</summary>
    void Remove();
}
