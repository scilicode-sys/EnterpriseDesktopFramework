using Framework.Core.Auth;

namespace Framework.Core.Rbac;

/// <summary>Écriture refusée parce que le rôle de la session ne la porte pas.</summary>
public sealed class AutorisationRefuseeException : InvalidOperationException
{
    public AutorisationRefuseeException(string permission, string? roleCle)
        : base($"Permission « {permission} » refusée{(string.IsNullOrWhiteSpace(roleCle) ? "" : $" pour le rôle « {roleCle} »")}.")
    {
        Permission = permission;
        RoleCle = roleCle;
    }

    public string Permission { get; }

    public string? RoleCle { get; }
}

/// <summary>
/// Lecture de la matrice pour la session courante. Volontairement sans état :
/// la matrice est figée à la construction, l'utilisateur vient du service de
/// session à chaque demande — fermer la session retire les droits aussitôt, sans
/// redémarrage ni cache à invalider.
/// </summary>
public sealed class AutorisationService : IAutorisationService
{
    private readonly IAuthService _auth;
    private readonly IMatriceRoles _matrice;

    public AutorisationService(IAuthService auth, IMatriceRoles matrice)
    {
        _auth = auth;
        _matrice = matrice;
    }

    public string? RoleCourant => _auth.CurrentUser?.Role;

    public bool EstConnecte => _auth.IsAuthenticated;

    public IReadOnlyList<string> PermissionsCourantes => _matrice.PermissionsDe(RoleCourant);

    public bool Peut(string permission) => _auth.IsAuthenticated && _matrice.EstAccordee(RoleCourant, permission);

    public void Exiger(string permission)
    {
        if (!Peut(permission))
        {
            throw new AutorisationRefuseeException(permission, RoleCourant);
        }
    }
}
