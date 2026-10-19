namespace Framework.Core.Rbac;

/// <summary>
/// Matrice déclarative : les rôles sont donnés au constructeur, l'aplatissement
/// de l'héritage est calculé à la construction (une seule fois, à chaud par
/// l'UI). Un cycle d'héritage est coupé au lieu de faire exploser la pile, et
/// la permission <see cref="Droits.Tout"/> accordée à un rôle rend les
/// suivantes superflues — c'est ce qu'on attend du rôle le plus haut.
/// </summary>
public sealed class MatriceRoles : IMatriceRoles
{
    private readonly Dictionary<string, RoleDefini> _parCle;
    private readonly Dictionary<string, HashSet<string>> _effectives;

    public IReadOnlyList<RoleDefini> Roles { get; }

    public MatriceRoles(IEnumerable<RoleDefini> roles)
    {
        Roles = roles.ToList();

        _parCle = new Dictionary<string, RoleDefini>(StringComparer.OrdinalIgnoreCase);

        foreach (var role in Roles)
        {
            if (_parCle.TryAdd(role.Cle, role) is false)
            {
                throw new InvalidOperationException(
                    $"Deux rôles déclarent la même clef « {role.Cle} » dans la matrice.");
            }
        }

        _effectives = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var role in Roles)
        {
            _effectives[role.Cle] = aplatir(role, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    public bool Connait(string? roleCle)
        => !string.IsNullOrWhiteSpace(roleCle) && _parCle.ContainsKey(roleCle);

    public bool EstAccordee(string? roleCle, string permission)
    {
        if (string.IsNullOrWhiteSpace(roleCle) || !_effectives.TryGetValue(roleCle, out var accordees))
        {
            return false;
        }

        return accordees.Contains(Droits.Tout) || accordees.Contains(permission);
    }

    public IReadOnlyList<string> PermissionsDe(string? roleCle)
        => roleCle is not null && _effectives.TryGetValue(roleCle, out var accordees)
            ? accordees.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList()
            : Array.Empty<string>();

    private HashSet<string> aplatir(RoleDefini role, ISet<string> vus)
    {
        var resultat = new HashSet<string>(role.Permissions, StringComparer.OrdinalIgnoreCase);

        if (!vus.Add(role.Cle))
        {
            return resultat;
        }

        foreach (var cleHeritee in role.RolesHerites)
        {
            // Un rôle hérité inexistant n'invente aucun droit : il est simplement ignoré.
            if (_parCle.TryGetValue(cleHeritee, out var herite))
            {
                foreach (var permission in aplatir(herite, vus))
                {
                    resultat.Add(permission);
                }
            }
        }

        return resultat;
    }
}
