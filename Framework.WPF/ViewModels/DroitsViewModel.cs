using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Framework.Core.Auth;
using Framework.Core.Data;
using Framework.Core.Rbac;
using Framework.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.WPF.ViewModels;

/// <summary>
/// Écran « Rôles et permissions » : la matrice déclarée par le produit, ce que la
/// session courante tient réellement, et qui porte chaque rôle sur ce poste.
/// C'est l'outil de contrôle du support — et la preuve visible qu'un rôle inconnu
/// de la matrice ne donne rien.
/// </summary>
public partial class DroitsViewModel : ObservableObject
{
    private const int TitulairesMaxAffiches = 6;

    private readonly IAuthService _auth;
    private readonly IMatriceRoles _matrice;
    private readonly IAutorisationService _autorisation;
    private readonly IServiceScopeFactory _portes;

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

    /// <summary>Pourquoi les comptes n'auraient pas été comptés.</summary>
    [ObservableProperty]
    private string _noteComptes = string.Empty;

    public bool EstAvertissementVisible => !string.IsNullOrEmpty(Avertissement);

    public bool EstNoteVisible => !string.IsNullOrEmpty(NoteComptes);

    public DroitsViewModel(
        IAuthService auth,
        IMatriceRoles matrice,
        IAutorisationService autorisation,
        IServiceScopeFactory portes)
    {
        _auth = auth;
        _matrice = matrice;
        _autorisation = autorisation;
        _portes = portes;
    }

    /// <summary>Appelé par la vue à son chargement, comme sur l'écran Exemples.</summary>
    public Task ChargerAsync() => chargerAsync();

    [RelayCommand]
    private Task RechargerAsync() => chargerAsync();

    private async Task chargerAsync()
    {
        // Écran atteignable par le bouton « retour » après un changement de session :
        // il refuse proprement d'afficher, il ne lève pas dans le Loaded d'une vue.
        if (!_autorisation.Peut(Droits.DroitsConsulter))
        {
            Roles = Array.Empty<RoleLigne>();
            SessionCourante = "(hors session)";
            Avertissement = "Accès refusé : cette session ne tient pas la permission « droits.consulter ».";
            NoteComptes = string.Empty;
            OnPropertyChanged(nameof(EstAvertissementVisible));
            return;
        }

        var roleCourant = _autorisation.RoleCourant;
        var (titulaires, note) = await recenserTitulairesAsync();

        Roles = _matrice.Roles
            .OrderByDescending(role => role.Cle.Equals(MatriceParDefaut.Administrateur, StringComparison.OrdinalIgnoreCase))
            .ThenBy(role => role.Cle, StringComparer.CurrentCultureIgnoreCase)
            .Select(role => new RoleLigne(
                role.Cle,
                role.Libelle,
                role.Description,
                role.RolesHerites.Count > 0 ? string.Join(", ", role.RolesHerites) : "aucun",
                _matrice.PermissionsDe(role.Cle),
                role.Cle.Equals(roleCourant, StringComparison.OrdinalIgnoreCase),
                titulaires is null ? null : compter(titulaires, role.Cle),
                titulaires is null ? null : resumer(titulaires, role.Cle)))
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

        NoteComptes = note;

        OnPropertyChanged(nameof(EstAvertissementVisible));
    }

    private sealed record CompteTitulaire(string Username, bool Actif);

    partial void OnNoteComptesChanged(string value) => OnPropertyChanged(nameof(EstNoteVisible));

    /// <summary>
    /// Les comptes du poste, groupés par rôle en une seule lecture. Null quand le
    /// rôle courant n'a rien à lire ou que la base ne répond pas : un écran de
    /// contrôle doit dire qu'il n'a pas compté, pas afficher « 0 ».
    /// </summary>
    private async Task<(Dictionary<string, List<CompteTitulaire>>? Titulaires, string Note)> recenserTitulairesAsync()
    {
        if (!_autorisation.Peut(Droits.ComptesConsulter))
        {
            return (null, "Comptes non listés : ce rôle ne tient pas « comptes.consulter ».");
        }

        try
        {
            await using var portee = _portes.CreateAsyncScope();
            var depot = portee.ServiceProvider.GetRequiredService<IUnitOfWork>().Repository<CompteEntity>();

            var groupés = new Dictionary<string, List<CompteTitulaire>>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var compte in await depot.FindAsync(_ => true))
            {
                if (!groupés.TryGetValue(compte.Role, out var listee))
                {
                    listee = new List<CompteTitulaire>();
                    groupés[compte.Role] = listee;
                }

                listee.Add(new CompteTitulaire(compte.Username, compte.Actif));
            }

            foreach (var listee in groupés.Values)
            {
                listee.Sort((a, b) => string.Compare(a.Username, b.Username, StringComparison.CurrentCultureIgnoreCase));
            }

            return (groupés, string.Empty);
        }
        catch (Exception)
        {
            return (null, "Comptes illisibles : le serveur de données n'a pas répondu. La matrice, elle, est bien affichée.");
        }
    }

    private static int compter(Dictionary<string, List<CompteTitulaire>> titulaires, string role)
        => titulaires.TryGetValue(role, out var listee) ? listee.Count : 0;

    private static string resumer(Dictionary<string, List<CompteTitulaire>> titulaires, string role)
    {
        if (!titulaires.TryGetValue(role, out var listee) || listee.Count == 0)
        {
            return "personne";
        }

        var désignés = listee
            .Take(TitulairesMaxAffiches)
            .Select(t => t.Actif ? t.Username : $"{t.Username} (désactivé)");

        var reste = listee.Count - TitulairesMaxAffiches;
        var queue = reste > 0 ? $" … {reste} autre{(reste > 1 ? "s" : "")}" : string.Empty;

        return string.Join(", ", désignés) + queue;
    }
}

/// <summary>Une ligne de la matrice, prête à afficher.</summary>
public sealed record RoleLigne(
    string Cle,
    string Libelle,
    string Description,
    string HeriteDe,
    IReadOnlyList<string> Permissions,
    bool EstRoleCourant,
    int? NbComptes,
    string? Titulaires)
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

    /// <summary>Qui porte ce rôle sur ce poste — ou pourquoi la réponse est inconnue.</summary>
    public string Porteurs => NbComptes is null || Titulaires is null
        ? "comptes non listés ici"
        : NbComptes == 0
            ? "aucun compte ne porte ce rôle"
            : $"{NbComptes} compte{(NbComptes > 1 ? "s" : "")} : {Titulaires}";
}
