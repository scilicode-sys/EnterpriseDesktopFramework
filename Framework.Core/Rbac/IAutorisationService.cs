using Framework.Core.Auth;

namespace Framework.Core.Rbac;

/// <summary>
/// Droits de la session en cours : le rôle vient de <see cref="IAuthService"/>,
/// la matrice vient de <see cref="IMatriceRoles"/>. Rien d'autre ne décide.
/// Une vue grise un bouton avec <see cref="Peut"/>, une écriture se protège
/// avec <see cref="Exiger"/> — les deux doivent exister, la vue seule n'est
/// jamais une sécurité.
/// </summary>
public interface IAutorisationService
{
    string? RoleCourant { get; }

    bool EstConnecte { get; }

    IReadOnlyList<string> PermissionsCourantes { get; }

    bool Peut(string permission);

    /// <summary>Lève <see cref="AutorisationRefuseeException"/> si la session n'a pas le droit.</summary>
    void Exiger(string permission);
}
