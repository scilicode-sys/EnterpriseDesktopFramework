namespace Framework.Core.Rbac;

/// <summary>
/// Rôle déclaré par la matrice. <see cref="RolesHerites"/> permet une échelle
/// (Administrateur → Utilisateur → Lecteur) sans recopier les permissions.
/// La clef est celle persistée dans <c>CompteEntity.Role</c> : elle est le seul
/// contrat entre la base, l'écran de connexion et la matrice.
/// </summary>
public sealed record RoleDefini(
    string Cle,
    string Libelle,
    IReadOnlyCollection<string> Permissions,
    IReadOnlyCollection<string> RolesHerites)
{
    public static readonly IReadOnlyCollection<string> Aucune = Array.Empty<string>();

    public RoleDefini(string cle, string libelle, IReadOnlyCollection<string> permissions)
        : this(cle, libelle, permissions, Aucune)
    {
    }

    /// <summary>Description courte affichée sur l'écran « Rôles et permissions ».</summary>
    public string Description { get; init; } = string.Empty;
}
