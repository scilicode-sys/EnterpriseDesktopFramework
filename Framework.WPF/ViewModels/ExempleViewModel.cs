using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Framework.Core.Data;
using Framework.Core.Notifications;
using Framework.Core.Sync;
using Framework.Data.Entities;
using Framework.WPF.Messages;
using Framework.WPF.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.WPF.ViewModels;

/// <summary>
/// Écran CRUD **de référence** du framework : liste filtrable, formulaire
/// création/modification, suppression logique, rafraîchissement temps réel
/// quand un autre poste a touché la table. Un produit dérivé copie ce fichier,
/// remplace <c>ExempleEntity</c> par son entité et garde la mécanique.
/// </summary>
/// <remarks>
/// L'Unit of Work n'est pas un champ : elle est résolue dans une portée DI
/// créée pour chaque opération et libérée aussitôt. Le contexte EF survit
/// ainsi exactement à son écriture — un écran laissé ouvert des heures ne retient
/// pas une connexion du pool.
/// </remarks>
public partial class ExempleViewModel : ObservableObject, IRecipient<ExternalEntityChangedMessage>
{
    private const string NomTable = "Exemples";

    private readonly IServiceScopeFactory _portes;
    private readonly IAlertService _alertes;

    public ObservableCollection<ExempleLigne> Lignes { get; } = new();

    [ObservableProperty]
    private string _filtre = string.Empty;

    [ObservableProperty]
    private ExempleLigne? _ligneSelectionnee;

    [ObservableProperty]
    private string _libelle = string.Empty;

    [ObservableProperty]
    private string _categorie = string.Empty;

    [ObservableProperty]
    private string _montant = "0,00";

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private bool _estEnEdition;

    [ObservableProperty]
    private bool _estOccupe;

    [ObservableProperty]
    private string _messageErreur = string.Empty;

    [ObservableProperty]
    private string _etat = "Prêt.";

    public int Total => Lignes.Count;

    /// <summary>Titre du formulaire — un seul texte, pas de chaîne de convertisseurs dans la vue.</summary>
    public string LibelleFormulaire => EstEnEdition ? "Modifier la ligne" : "Créer une ligne";

    public ExempleViewModel(IServiceScopeFactory portes, IAlertService alertes)
    {
        _portes = portes;
        _alertes = alertes;

        WeakReferenceMessenger.Default.Register(this);
    }

    /// <summary>Appelé par la vue à son chargement, comme sur l'écran de connexion.</summary>
    public Task ChargerAsync() => rechargerAsync();

    [RelayCommand]
    private Task RechargerAsync() => rechargerAsync();

    [RelayCommand]
    private void Nouveau()
    {
        LigneSelectionnee = null;
        EstEnEdition = false;
        Libelle = string.Empty;
        Categorie = string.Empty;
        Montant = "0,00";
        Note = string.Empty;
        MessageErreur = string.Empty;
        Etat = "Nouvelle ligne.";
    }

    [RelayCommand]
    private async Task EnregistrerAsync()
    {
        if (!valider())
        {
            return;
        }

        var libelle = Libelle.Trim();
        var categorie = Categorie.Trim();
        var note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();
        var montant = decimal.Parse(Montant, NumberStyles.Number, CultureInfo.CurrentCulture);
        var id = LigneSelectionnee?.Id;

        try
        {
            EstOccupe = true;
            MessageErreur = string.Empty;

            await using var portee = _portes.CreateAsyncScope();
            var unTravail = portee.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var depot = unTravail.Repository<ExempleEntity>();

            if (id is null)
            {
                await depot.AddAsync(new ExempleEntity
                {
                    Libelle = libelle,
                    Categorie = categorie,
                    Montant = montant,
                    Note = note,
                });

                Etat = $"« {libelle} » créée.";
            }
            else
            {
                var existante = await depot.GetByIdAsync(id.Value);

                if (existante is null)
                {
                    MessageErreur = "Cette ligne a disparu de la base (supprimée entre-temps).";
                    return;
                }

                // L'entité est suivie par ce contexte : ses champs mutés suffisent,
                // Update() est réservé aux objets détachés — et nous n'en avons pas ici.
                existante.Libelle = libelle;
                existante.Categorie = categorie;
                existante.Montant = montant;
                existante.Note = note;

                Etat = $"« {libelle} » enregistrée.";
            }

            await unTravail.SaveChangesAsync();
            Nouveau();
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
        }
    }

    [RelayCommand]
    private async Task SupprimerAsync()
    {
        var cible = LigneSelectionnee;

        if (cible is null)
        {
            MessageErreur = "Sélectionnez une ligne à supprimer.";
            return;
        }

        try
        {
            EstOccupe = true;
            MessageErreur = string.Empty;

            await using var portee = _portes.CreateAsyncScope();
            var unTravail = portee.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var depot = unTravail.Repository<ExempleEntity>();

            var entite = await depot.GetByIdAsync(cible.Id);

            if (entite is null)
            {
                MessageErreur = "Cette ligne a déjà été supprimée.";
                return;
            }

            // Remove n'efface rien : la convention du contexte convertit le DELETE
            // en IsDeleted = true, et le filtre global la cache à toute requête.
            depot.Remove(entite);
            await unTravail.SaveChangesAsync();

            Etat = $"« {cible.Libelle} » supprimée.";
            Nouveau();
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
        }
    }

    public void Receive(ExternalEntityChangedMessage message)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            async () => await ReactualiserSiNotreTableAsync(message.Notification));
    }

    /// <summary>
    /// Half de <see cref="Receive"/> : la partie testable, découplée du
    /// Dispatcher WPF. Une notification sur une autre table ne doit pas faire
    /// boucler cet écran, et une écriture en cours sur ce poste ne doit pas
    /// être percutée — la fin de commande recharge déjà l'état vrai.
    /// </summary>
    public async Task ReactualiserSiNotreTableAsync(SyncNotificationDto notification)
    {
        if (!string.Equals(notification.Table, NomTable, StringComparison.Ordinal) || EstOccupe)
        {
            return;
        }

        await rechargerAsync();
        Etat = "Liste rechargée : un autre poste a modifié la table « Exemples ».";
    }

    partial void OnLigneSelectionneeChanged(ExempleLigne? value)
    {
        if (value is null)
        {
            EstEnEdition = false;
            OnPropertyChanged(nameof(LibelleFormulaire));
            return;
        }

        EstEnEdition = true;
        MessageErreur = string.Empty;
        Libelle = value.Libelle;
        Categorie = value.Categorie;
        Montant = value.Montant.ToString("0.00", CultureInfo.CurrentCulture);
        Note = value.Note ?? string.Empty;
        OnPropertyChanged(nameof(LibelleFormulaire));
    }

    private bool valider()
    {
        MessageErreur = string.Empty;

        if (string.IsNullOrWhiteSpace(Libelle))
        {
            MessageErreur = "Le libellé est obligatoire.";
            return false;
        }

        if (Libelle.Trim().Length > 128)
        {
            MessageErreur = "Le libellé ne peut pas dépasser 128 caractères.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Categorie))
        {
            MessageErreur = "La catégorie est obligatoire.";
            return false;
        }

        if (Categorie.Trim().Length > 32)
        {
            MessageErreur = "La catégorie ne peut pas dépasser 32 caractères.";
            return false;
        }

        if (!decimal.TryParse(Montant, NumberStyles.Number, CultureInfo.CurrentCulture, out var montant))
        {
            MessageErreur = "Le montant doit être un nombre, séparateur décimal compris.";
            return false;
        }

        if (decimal.Round(montant, 2) != montant)
        {
            MessageErreur = "Le montant est limité à deux décimales.";
            return false;
        }

        if (Note.Trim().Length > 512)
        {
            MessageErreur = "La note ne peut pas dépasser 512 caractères.";
            return false;
        }

        return true;
    }

    private bool _rechargementEnCours;

    private async Task rechargerAsync()
    {
        // Le filtre frappe à chaque frappe et l'autre poste à chaque notification :
        // sans ce garde-fou, deux requêtes se marcheraient dessus et la liste
        // finirait dans un état qui ne correspond à aucune des deux.
        if (_rechargementEnCours)
        {
            return;
        }

        _rechargementEnCours = true;
        var filtre = Filtre.Trim();

        try
        {
            await using var portee = _portes.CreateAsyncScope();
            var unTravail = portee.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var depot = unTravail.Repository<ExempleEntity>();

            var trouvees = string.IsNullOrEmpty(filtre)
                ? await depot.FindAsync(_ => true)
                : await depot.FindAsync(e =>
                    e.Libelle.ToLower().Contains(filtre.ToLower())
                    || e.Categorie.ToLower().Contains(filtre.ToLower()));

            var fraiches = trouvees
                .OrderBy(e => e.Categorie, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(e => e.Libelle, StringComparer.CurrentCultureIgnoreCase)
                .Select(e => new ExempleLigne(
                    e.Id, e.Libelle, e.Categorie, e.Montant, e.Note,
                    e.UpdatedAt ?? e.CreatedAt))
                .ToList();

            Lignes.Clear();
            foreach (var ligne in fraiches)
            {
                Lignes.Add(ligne);
            }

            OnPropertyChanged(nameof(Total));
            Etat = $"{Lignes.Count} ligne{(Lignes.Count > 1 ? "s" : "")} affichée{(Lignes.Count > 1 ? "s" : "")}.";
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
