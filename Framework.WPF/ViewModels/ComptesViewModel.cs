using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Framework.Core.Auth;
using Framework.Core.Data;
using Framework.Core.Notifications;
using Framework.Core.Rbac;
using Framework.Core.Security;
using Framework.Core.Sync;
using Framework.Data.Entities;
using Framework.WPF.Messages;
using Framework.WPF.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.WPF.ViewModels;

/// <summary>
/// Écran « Comptes » : un Administrateur crée, inactive, change le rôle et dégèle
/// un compte **sans toucher à SQL**. Il reprend mécaniquement l'écran CRUD de
/// référence — portée DI par écriture, <see cref="IAutorisationService.Exiger"/>
/// comme vraie garde, rechargement sur notification d'un autre poste.
/// </summary>
/// <remarks>
/// Deux gardes n'existent pas sur l'écran de référence et sont ici indispensables :
/// changer son propre rôle, ou désactiver / supprimer le compte dont ce poste tient
/// la session, vide la barre latérale sans laisser de retour possible. Le verrouillage
/// par l'écran lui-même serait un défaut de conception, pas une protection.
/// </remarks>
public partial class ComptesViewModel : ObservableObject, IRecipient<ExternalEntityChangedMessage>
{
    private const string NomTable = "Comptes";

    /// <summary>Plancher posé ici, pas en base : un compte créé depuis l'écran doit être assez solide pour tenir l'anti-force-brute.</summary>
    private const int TailleMinimaleMotDePasse = 8;

    private readonly IServiceScopeFactory _portes;
    private readonly IAlertService _alertes;
    private readonly IAutorisationService _autorisation;
    private readonly IAuthService _auth;
    private readonly IMatriceRoles _matrice;

    public ObservableCollection<CompteLigne> Lignes { get; } = new();

    [ObservableProperty]
    private string _filtre = string.Empty;

    [ObservableProperty]
    private CompteLigne? _ligneSelectionnee;

    [ObservableProperty]
    private string _identifiant = string.Empty;

    [ObservableProperty]
    private string _nomComplet = string.Empty;

    [ObservableProperty]
    private string _roleSelectionne = string.Empty;

    /// <summary>Poussé par le code-behind (PasswordBox) : jamais lié, jamais relu depuis la base.</summary>
    [ObservableProperty]
    private string _motDePasse = string.Empty;

    [ObservableProperty]
    private string _confirmation = string.Empty;

    [ObservableProperty]
    private bool _actif = true;

    [ObservableProperty]
    private bool _estEnEdition;

    [ObservableProperty]
    private bool _estOccupe;

    [ObservableProperty]
    private string _messageErreur = string.Empty;

    [ObservableProperty]
    private string _etat = "Prêt.";

    /// <summary>Porte de nettoyage des deux PasswordBox : la vue l'écoute, le mot de passe ne se lie pas.</summary>
    [ObservableProperty]
    private int _generationMotDePasse;

    public int Total => Lignes.Count;

    /// <summary>Rôles proposés au choix : uniquement ceux déclarés dans la matrice.</summary>
    public IReadOnlyList<string> Roles => _matrice.Roles
        .OrderByDescending(role => role.Cle.Equals(MatriceParDefaut.Administrateur, StringComparison.OrdinalIgnoreCase))
        .ThenBy(role => role.Cle, StringComparer.CurrentCultureIgnoreCase)
        .Select(role => role.Cle)
        .ToList();

    public string LibelleFormulaire => EstEnEdition ? "Modifier le compte" : "Créer un compte";

    /// <summary>Texte du champ mot de passe : la règle n'est pas la même à la création et à la modification.</summary>
    public string ConsigneMotDePasse => EstEnEdition
        ? "Laissez vide pour conserver le mot de passe actuel."
        : $"{TailleMinimaleMotDePasse} caractères minimum.";

    public bool PeutGerer => _autorisation.Peut(Droits.ComptesGerer);

    public bool BoutonEnregistrerActif => !EstOccupe && PeutGerer;

    public bool BoutonBasculerActif => EstEnEdition && PeutGerer;

    public bool BoutonSupprimerActif => EstEnEdition && PeutGerer;

    public bool BoutonDegelerActif => EstEnEdition && PeutGerer && LigneSelectionnee?.GelJusqua is not null;

    public ComptesViewModel(
        IServiceScopeFactory portes,
        IAlertService alertes,
        IAutorisationService autorisation,
        IAuthService auth,
        IMatriceRoles matrice)
    {
        _portes = portes;
        _alertes = alertes;
        _autorisation = autorisation;
        _auth = auth;
        _matrice = matrice;

        WeakReferenceMessenger.Default.Register(this);
    }

    /// <summary>Appelé par la vue à son chargement.</summary>
    public Task ChargerAsync() => rechargerAsync();

    [RelayCommand]
    private Task RechargerAsync() => rechargerAsync();

    [RelayCommand]
    private void Nouveau()
    {
        LigneSelectionnee = null;
        EstEnEdition = false;
        Identifiant = string.Empty;
        NomComplet = string.Empty;
        RoleSelectionne = _matrice.Connait(_autorisation.RoleCourant)
            ? _autorisation.RoleCourant!
            : MatriceParDefaut.Utilisateur;
        Actif = true;
        MessageErreur = string.Empty;
        Etat = "Nouveau compte.";
        GenerationMotDePasse++;
    }

    [RelayCommand]
    private async Task EnregistrerAsync()
    {
        if (!valider())
        {
            return;
        }

        var username = Identifiant.Trim();
        var nomComplet = NomComplet.Trim();
        var role = RoleSelectionne;
        var motDePasse = MotDePasse;
        var id = LigneSelectionnee?.Id;
        string? resultat = null;

        try
        {
            _autorisation.Exiger(Droits.ComptesGerer);

            EstOccupe = true;
            MessageErreur = string.Empty;

            await using var portee = _portes.CreateAsyncScope();
            var unTravail = portee.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var depot = unTravail.Repository<CompteEntity>();

            var homonymes = await depot.FindAsync(c => c.Username == username);

            if (homonymes.Any(c => c.Id != id))
            {
                MessageErreur = $"L'identifiant « {username} » est déjà porté par un compte.";
                return;
            }

            if (id is null)
            {
                await depot.AddAsync(new CompteEntity
                {
                    Username = username,
                    NomComplet = nomComplet,
                    Role = role,
                    PasswordHash = PasswordHasher.Hash(motDePasse),
                    Actif = Actif,
                });

                resultat = $"« {username} » créé avec le rôle « {role} ».";
            }
            else
            {
                var existant = await depot.GetByIdAsync(id.Value);

                if (existant is null)
                {
                    MessageErreur = "Ce compte a disparu de la base (supprimé entre-temps).";
                    return;
                }

                existant.Username = username;
                existant.NomComplet = nomComplet;
                existant.Role = role;
                existant.Actif = Actif;

                if (!string.IsNullOrEmpty(motDePasse))
                {
                    existant.PasswordHash = PasswordHasher.Hash(motDePasse);

                    // Un mot de passe posé depuis cet écran est un mot de passe choisi :
                    // le gel et les échecs accumulés sous l'ancien n'ont plus de raison d'être.
                    existant.EchecsConsecutifs = 0;
                    existant.GelJusqua = null;
                }

                resultat = $"« {username} » enregistré.";
            }

            await unTravail.SaveChangesAsync();
            Nouveau();
        }
        catch (AutorisationRefuseeException)
        {
            MessageErreur = Refus("enregistrer un compte");
        }
        catch (Exception ex)
        {
            MessageErreur = "Enregistrement impossible. Réessayez.";
            _alertes.Error(ex.Message);
        }
        finally
        {
            EstOccupe = false;
            await rechargerAsync();
            clore(resultat);
        }
    }

    [RelayCommand]
    private async Task BasculerActifAsync()
    {
        var cible = LigneSelectionnee;

        if (cible is null)
        {
            MessageErreur = "Sélectionnez un compte à activer ou désactiver.";
            return;
        }

        string? resultat = null;

        try
        {
            _autorisation.Exiger(Droits.ComptesGerer);

            if (EstLeCompteCourant(cible.Id))
            {
                MessageErreur = "Impossible d'inactiver le compte dont ce poste tient la session : "
                    + "plus aucun écran ne lui serait accessible, et seul SQL pourrait le réactiver.";
                return;
            }

            EstOccupe = true;
            MessageErreur = string.Empty;

            await using var portee = _portes.CreateAsyncScope();
            var unTravail = portee.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var depot = unTravail.Repository<CompteEntity>();

            var entite = await depot.GetByIdAsync(cible.Id);

            if (entite is null)
            {
                MessageErreur = "Ce compte a déjà été supprimé.";
                return;
            }

            entite.Actif = !entite.Actif;
            await unTravail.SaveChangesAsync();

            resultat = $"« {entite.Username} » "
                       + (entite.Actif
                           ? "activé : il peut de nouveau se connecter."
                           : "désactivé : sa connexion est désormais refusée.");
            Nouveau();
        }
        catch (AutorisationRefuseeException)
        {
            MessageErreur = Refus("activer ou désactiver un compte");
        }
        catch (Exception ex)
        {
            MessageErreur = "Opération impossible. Réessayez.";
            _alertes.Error(ex.Message);
        }
        finally
        {
            EstOccupe = false;
            await rechargerAsync();
            clore(resultat);
        }
    }

    [RelayCommand]
    private async Task DegelerAsync()
    {
        var cible = LigneSelectionnee;

        if (cible is null)
        {
            MessageErreur = "Sélectionnez un compte à dégeler.";
            return;
        }

        string? resultat = null;

        try
        {
            _autorisation.Exiger(Droits.ComptesGerer);

            EstOccupe = true;
            MessageErreur = string.Empty;

            await using var portee = _portes.CreateAsyncScope();
            var unTravail = portee.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var depot = unTravail.Repository<CompteEntity>();

            var entite = await depot.GetByIdAsync(cible.Id);

            if (entite is null)
            {
                MessageErreur = "Ce compte a déjà été supprimé.";
                return;
            }

            // Le gel de l'anti-force-brute se lève tout seul en soixante secondes ;
            // ce bouton n'est pas une porte dérobée, il évite à l'exploitant d'écrire
            // un UPDATE sur une table qu'un écran sait désormais tenir.
            entite.EchecsConsecutifs = 0;
            entite.GelJusqua = null;
            await unTravail.SaveChangesAsync();

            resultat = $"« {entite.Username} » dégagé de son gel : il peut retenter une connexion.";
            Nouveau();
        }
        catch (AutorisationRefuseeException)
        {
            MessageErreur = Refus("dégeler un compte");
        }
        catch (Exception ex)
        {
            MessageErreur = "Opération impossible. Réessayez.";
            _alertes.Error(ex.Message);
        }
        finally
        {
            EstOccupe = false;
            await rechargerAsync();
            clore(resultat);
        }
    }

    [RelayCommand]
    private async Task SupprimerAsync()
    {
        var cible = LigneSelectionnee;

        if (cible is null)
        {
            MessageErreur = "Sélectionnez un compte à supprimer.";
            return;
        }

        string? resultat = null;

        try
        {
            _autorisation.Exiger(Droits.ComptesGerer);

            if (EstLeCompteCourant(cible.Id))
            {
                MessageErreur = "Impossible de supprimer le compte dont ce poste tient la session : "
                    + "l'amorce du premier compte est fermée, seul SQL pourrait rouvrir le déploiement.";
                return;
            }

            EstOccupe = true;
            MessageErreur = string.Empty;

            await using var portee = _portes.CreateAsyncScope();
            var unTravail = portee.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var depot = unTravail.Repository<CompteEntity>();

            var entite = await depot.GetByIdAsync(cible.Id);

            if (entite is null)
            {
                MessageErreur = "Ce compte a déjà été supprimé.";
                return;
            }

            // Remove n'efface rien : la convention du contexte convertit le DELETE
            // en IsDeleted = true, et le filtre global la cache à toute requête.
            depot.Remove(entite);
            await unTravail.SaveChangesAsync();

            resultat = $"« {cible.Username} » supprimé : il ne peut plus se connecter.";
            Nouveau();
        }
        catch (AutorisationRefuseeException)
        {
            MessageErreur = Refus("supprimer un compte");
        }
        catch (Exception ex)
        {
            MessageErreur = "Suppression impossible. Réessayez.";
            _alertes.Error(ex.Message);
        }
        finally
        {
            EstOccupe = false;
            await rechargerAsync();
            clore(resultat);
        }
    }

    public void Receive(ExternalEntityChangedMessage message)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            async () => await ReactualiserSiNotreTableAsync(message.Notification));
    }

    /// <summary>
    /// Half de <see cref="Receive"/> : la partie testable, découplée du Dispatcher WPF.
    /// C'est par cette méthode que l'exploitant vérifie le critère « un poste ouvert
    /// voit la liste des comptes bouger quand un autre les a modifiés ».
    /// </summary>
    public async Task ReactualiserSiNotreTableAsync(SyncNotificationDto notification)
    {
        if (!string.Equals(notification.Table, NomTable, StringComparison.Ordinal) || EstOccupe)
        {
            return;
        }

        await rechargerAsync();
        Etat = "Liste rechargée : un autre poste a modifié la table « Comptes ».";
    }

    partial void OnEstOccupeChanged(bool value)
    {
        OnPropertyChanged(nameof(BoutonEnregistrerActif));
        OnPropertyChanged(nameof(BoutonBasculerActif));
        OnPropertyChanged(nameof(BoutonDegelerActif));
        OnPropertyChanged(nameof(BoutonSupprimerActif));
    }

    partial void OnEstEnEditionChanged(bool value)
    {
        OnPropertyChanged(nameof(ConsigneMotDePasse));
        OnPropertyChanged(nameof(BoutonBasculerActif));
        OnPropertyChanged(nameof(BoutonDegelerActif));
        OnPropertyChanged(nameof(BoutonSupprimerActif));
    }

    partial void OnLigneSelectionneeChanged(CompteLigne? value)
    {
        if (value is null)
        {
            EstEnEdition = false;
            OnPropertyChanged(nameof(LibelleFormulaire));
            return;
        }

        EstEnEdition = true;
        MessageErreur = string.Empty;
        Identifiant = value.Username;
        NomComplet = value.NomComplet;
        RoleSelectionne = value.Role;
        Actif = value.Actif;

        // Le hash n'est jamais remonté vers la vue ; le champ reste vide, et vide
        // veut dire « conserver » côté modification.
        MotDePasse = string.Empty;
        Confirmation = string.Empty;
        GenerationMotDePasse++;

        OnPropertyChanged(nameof(LibelleFormulaire));
        OnPropertyChanged(nameof(BoutonDegelerActif));
    }

    private bool valider()
    {
        MessageErreur = string.Empty;

        if (string.IsNullOrWhiteSpace(Identifiant))
        {
            MessageErreur = "L'identifiant de connexion est obligatoire.";
            return false;
        }

        var username = Identifiant.Trim();

        if (username.Length > 64)
        {
            MessageErreur = "L'identifiant ne peut pas dépasser 64 caractères.";
            return false;
        }

        if (username.Any(char.IsWhiteSpace))
        {
            MessageErreur = "L'identifiant ne peut pas contenir d'espace : c'est lui que l'on tape à la connexion.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(NomComplet))
        {
            MessageErreur = "Le nom complet est obligatoire.";
            return false;
        }

        if (NomComplet.Trim().Length > 128)
        {
            MessageErreur = "Le nom complet ne peut pas dépasser 128 caractères.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(RoleSelectionne))
        {
            MessageErreur = "Choisissez un rôle dans la liste.";
            return false;
        }

        // Un rôle hors matrice se connecterait mais ne tiendrait aucun écran :
        // la liste est fermée, c'est précisément ce qu'elle doit rester.
        if (!_matrice.Connait(RoleSelectionne))
        {
            MessageErreur = $"Le rôle « {RoleSelectionne} » n'est pas déclaré dans la matrice : "
                            + "il ne donnerait accès à aucun écran.";
            return false;
        }

        if (LigneSelectionnee is null && string.IsNullOrEmpty(MotDePasse))
        {
            MessageErreur = "Le mot de passe est obligatoire à la création d'un compte.";
            return false;
        }

        if (!string.IsNullOrEmpty(MotDePasse) && MotDePasse.Length < TailleMinimaleMotDePasse)
        {
            MessageErreur = $"Le mot de passe doit compter au moins {TailleMinimaleMotDePasse} caractères.";
            return false;
        }

        if (!string.IsNullOrEmpty(MotDePasse) && MotDePasse != Confirmation)
        {
            MessageErreur = "La confirmation ne correspond pas au mot de passe.";
            return false;
        }

        if (LigneSelectionnee is { } cible
            && EstLeCompteCourant(cible.Id)
            && !string.Equals(RoleSelectionne, _auth.CurrentUser?.Role, StringComparison.Ordinal))
        {
            MessageErreur = "Vous ne pouvez pas changer votre propre rôle : la matrice ne vous laisserait "
                            + "plus ouvrir cet écran, et seul SQL pourrait vous y ramener.";
            return false;
        }

        return true;
    }

    /// <summary>La session de CE poste, lue à l'instant où la garde décide — pas la ligne chargée à l'écran.</summary>
    private bool EstLeCompteCourant(Guid id)
        => _auth.CurrentUser is { } utilisateur && utilisateur.Id == id;

    /// <summary>
    /// Rend l'écran à l'opération qui vient de réussir : <see cref="rechargerAsync"/>
    /// termine en écrivant son comptage dans <c>Etat</c>, ce qui effacerait la trace
    /// de ce que l'exploitant a fait s'il ne restait que « 3 comptes affichés ».
    /// </summary>
    private void clore(string? resultat)
    {
        if (resultat is not null)
        {
            Etat = resultat;
        }
    }

    /// <summary>Phrase d'écran d'un refus : la clef de permission reste au journal, pas au poste de travail.</summary>
    private string Refus(string action)
        => $"Droits insuffisants pour {action} depuis ce poste (rôle « {_autorisation.RoleCourant ?? "(hors session)"} »).";

    private bool _rechargementEnCours;

    private async Task rechargerAsync()
    {
        if (!_autorisation.Peut(Droits.ComptesConsulter))
        {
            Lignes.Clear();
            OnPropertyChanged(nameof(Total));
            Etat = Refus("consulter les comptes");
            return;
        }

        // Le bouton « Actualiser » et la notification d'un autre poste peuvent se
        // croiser sur une table que deux administrateurs ouvrent en même temps.
        if (_rechargementEnCours)
        {
            return;
        }

        _rechargementEnCours = true;
        var filtre = Filtre.Trim();
        var session = _auth.CurrentUser;

        try
        {
            await using var portee = _portes.CreateAsyncScope();
            var unTravail = portee.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var depot = unTravail.Repository<CompteEntity>();

            var trouves = string.IsNullOrEmpty(filtre)
                ? await depot.FindAsync(_ => true)
                : await depot.FindAsync(c =>
                    c.Username.ToLower().Contains(filtre.ToLower())
                    || c.NomComplet.ToLower().Contains(filtre.ToLower())
                    || c.Role.ToLower().Contains(filtre.ToLower()));

            var fraiches = trouves
                .OrderBy(c => c.Username, StringComparer.CurrentCultureIgnoreCase)
                .Select(c => new CompteLigne(
                    c.Id, c.Username, c.NomComplet, c.Role, c.Actif,
                    c.EchecsConsecutifs, c.GelJusqua, c.UpdatedAt ?? c.CreatedAt,
                    session is not null && session.Id == c.Id))
                .ToList();

            Lignes.Clear();
            foreach (var ligne in fraiches)
            {
                Lignes.Add(ligne);
            }

            OnPropertyChanged(nameof(Total));
            Etat = $"{Lignes.Count} compte{(Lignes.Count > 1 ? "s" : "")} affiché{(Lignes.Count > 1 ? "s" : "")}.";
        }
        catch (Exception ex)
        {
            MessageErreur = "Serveur de données injoignable : la liste est restée en l'état.";
            _alertes.Error(ex.Message);
        }
        finally
        {
            _rechargementEnCours = false;
        }
    }
}
