using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Framework.Core.Auth;
using Framework.Core.Rbac;

namespace Framework.WPF.ViewModels;

/// <summary>
/// Écran « Rôles et permissions » : lecture de la matrice déclarée par le
/// produit, plus ce que la session courante tient réellement. C'est l'outil de
/// contrôle du support — et la preuve visible qu'un rôle inconnu de la matrice
/// ne donne rien.
/// </summary>
public partial class DroitsViewModel : ObservableObject
{
    private readonly IAuthService _auth;
    private readonly IMatriceRoles _matrice;
    private readonly IAutorisationService _autorisation;

    /// <summary>
    /// La liste déroulée par le bouton « Recharger » doit se repeindre : sans
    /// notification, la vue garde la liste déjà affichée et le bouton semble mort.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<RoleLigne> _roles = Array.Empty<RoleLigne>();

    [ObservableProperty]
    private string _sessionCourante = "(hors session)";

    [ObservableProperty]
    private string _avertissement = string.Empty;

    public bool EstAvertissementVisible => !string.IsNullOrEmpty(Avertissement);

    public DroitsViewModel(IAuthService auth, IMatriceRoles matrice, IAutorisationService autorisation)
    {
        _auth = auth;
        _matrice = matrice;
        _autorisation = autorisation;
    }

    /// <summary>Appelé par la vue à son chargement, comme sur l'écran Exemples.</summary>
    public void Charger()
    {
        // Écran atteignable par le bouton « retour » après un changement de session :
        // il refuse proprement d'afficher, il ne lève pas dans le Loaded d'une vue.
        if (!_autorisation.Peut(Droits.DroitsConsulter))
        {
            Roles = Array.Empty<RoleLigne>();
            SessionCourante = "(hors session)";
            Avertissement = "Accès refusé : cette session ne tient pas la permission « droits.consulter ».";
            OnPropertyChanged(nameof(EstAvertissementVisible));
            return;
        }

        var roleCourant = _autorisation.RoleCourant;

        Roles = _matrice.Roles
            .OrderByDescending(role => role.Cle.Equals(MatriceParDefaut.Administrateur, StringComparison.OrdinalIgnoreCase))
            .ThenBy(role => role.Cle, StringComparer.CurrentCultureIgnoreCase)
            .Select(role => new RoleLigne(
                role.Cle,
                role.Libelle,
                role.Description,
                role.RolesHerites.Count > 0 ? string.Join(", ", role.RolesHerites) : "aucun",
                _matrice.PermissionsDe(role.Cle),
                role.Cle.Equals(roleCourant, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        SessionCourante = _auth.CurrentUser is { } utilisateur
            ? $"{utilisateur.NomComplet} — rôle « {utilisateur.Role} »"
            : "(hors session)";

        // Le rôle de la session est forcément déclaré puisqu'il tient « droits.consulter ».
        // Ce qui se déclare, lui, est un rôle du produit qui ne mène nulle part : sans
        // permission effective, la personne qui le porte reste devant un écran vide.
        var sansAucunDroit = _matrice.Roles
            .Where(role => _matrice.PermissionsDe(role.Cle).Count == 0)
            .Select(role => $"« {role.Cle} »")
            .ToList();

        Avertissement = sansAucunDroit.Count == 0
            ? string.Empty
            : $"{string.Join(", ", sansAucunDroit)} "
              + $"{(sansAucunDroit.Count > 1 ? "ne donnent" : "ne donne")} accès à aucun écran protégé "
              + "de ce poste : à compléter dans la matrice ou à retirer.";

        OnPropertyChanged(nameof(EstAvertissementVisible));
    }

    [RelayCommand]
    private void Recharger() => Charger();
}

/// <summary>Une ligne de la matrice, prête à afficher.</summary>
public sealed record RoleLigne(
    string Cle,
    string Libelle,
    string Description,
    string HeriteDe,
    IReadOnlyList<string> Permissions,
    bool EstRoleCourant)
{
    /// <summary>La sauvageonne se compte, elle ne se liste pas : « toutes » plutôt que « 1 ».</summary>
    public string Compte => Permissions.Contains(Droits.Tout)
        ? "toutes permissions"
        : Permissions.Count == 0
            ? "aucune permission"
            : $"{Permissions.Count} permission{(Permissions.Count > 1 ? "s" : "")}";

    public string Resume => Permissions.Contains(Droits.Tout)
        ? "« * » : tous les écrans, toutes les écritures, y compris ceux ajoutés par le produit"
        : Permissions.Count == 0
            ? "—"
            : string.Join(" · ", Permissions);
}
