namespace Framework.Core.Rbac;

/// <summary>
/// Matrice rôles → permissions du framework. Elle est déclarée en code et non
/// en base : un rôle inconnu d'elle ne donne aucun droit, comportement sûr par
/// défaut quand une migration ancienne ou une saisie libre apporte une valeur
/// que le socle ne connaît pas.
/// </summary>
public interface IMatriceRoles
{
    IReadOnlyList<RoleDefini> Roles { get; }

    /// <summary>Le rôle est-il déclaré dans la matrice ? (un rôle absent = zéro droit, mais l'écran doit pouvoir le dire)</summary>
    bool Connait(string? roleCle);

    /// <summary>Le rôle, héritage inclus, possède-t-il cette permission ?</summary>
    bool EstAccordee(string? roleCle, string permission);

    /// <summary>Permissions effectives, héritage aplati, sans doublon, triées.</summary>
    IReadOnlyList<string> PermissionsDe(string? roleCle);
}
