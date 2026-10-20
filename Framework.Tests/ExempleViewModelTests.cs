using System.Globalization;
using Framework.Core.Data;
using Framework.Core.Notifications;
using Framework.Core.Rbac;
using Framework.Core.Sync;
using Framework.Data;
using Framework.Data.Entities;
using Framework.WPF.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Framework.Tests;

/// <summary>
/// Écran CRUD de référence : validations, écritures par Unit of Work, suppression
/// logique réelle en base, rafraîchissement temps réel déclenché par une notification
/// d'un autre poste, et portes de la matrice de droits. EF InMemory suffit — le SQL
/// reste l'affaire des postes, comme pour AuthServiceTests.
/// </summary>
public sealed class ExempleViewModelTests : IDisposable
{
    private readonly DbContextOptions<FrameworkDbContext> _options;
    private readonly ServiceProvider _portes;
    private readonly AlertesEnMemoire _alertes = new();
    private readonly FakeSession _session = new(MatriceParDefaut.Administrateur);

    public ExempleViewModelTests()
    {
        _options = new DbContextOptionsBuilder<FrameworkDbContext>()
            .UseInMemoryDatabase($"framework-exemple-{Guid.NewGuid():N}")
            // L'audit n'est plus dans le contexte : un poste le déclare via
            // AddFrameworkData, un test le déclare ici.
            .AddInterceptors(new AuditSaveChangesInterceptor(_session, new FakeHorloge()))
            .Options;

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<FrameworkDbContext>>(new FabriqueContexte(_options));
        services.AddTransient<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IAlertService>(_alertes);
        _portes = services.BuildServiceProvider();
    }

    public void Dispose() => _portes.Dispose();

    /// <summary>Session Administrateur par défaut ; les refus se testent en changeant le rôle.</summary>
    private ExempleViewModel creerEcran(string? role = null)
    {
        if (role is not null)
        {
            _session.SeConnecter(role);
        }

        return new ExempleViewModel(
            _portes.GetRequiredService<IServiceScopeFactory>(),
            _alertes,
            new AutorisationService(_session, MatriceParDefaut.Creer()));
    }

    private async Task semerAsync(params ExempleEntity[] entites)
    {
        await using var contexte = new FrameworkDbContext(_options);
        contexte.Exemples.AddRange(entites);
        await contexte.SaveChangesAsync();
    }

    private async Task<ExempleEntity?> lireEnBaseAsync(Guid id)
    {
        await using var contexte = new FrameworkDbContext(_options);
        return await contexte.Exemples.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == id);
    }

    private async Task<List<ExempleEntity>> toutesEnBaseAsync()
    {
        await using var contexte = new FrameworkDbContext(_options);
        return await contexte.Exemples.IgnoreQueryFilters().ToListAsync();
    }

    private static ExempleEntity exemple(string libelle, string categorie, decimal montant = 10m)
        => new() { Libelle = libelle, Categorie = categorie, Montant = montant };

    private static string montant(decimal valeur)
        => valeur.ToString("0.00", CultureInfo.CurrentCulture);

    [Fact]
    public async Task Enregistrer_LibelleVide_RefuseSansRienEcrire()
    {
        var ecran = creerEcran();
        ecran.Categorie = "Stock";

        await ecran.EnregistrerCommand.ExecuteAsync(null);

        Assert.Contains("libellé", ecran.MessageErreur, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(ecran.Lignes);
        Assert.Empty(await toutesEnBaseAsync());
    }

    [Fact]
    public async Task Enregistrer_CategorieVide_Refuse()
    {
        var ecran = creerEcran();
        ecran.Libelle = "Alpha";

        await ecran.EnregistrerCommand.ExecuteAsync(null);

        Assert.Contains("catégorie", ecran.MessageErreur, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await toutesEnBaseAsync());
    }

    [Fact]
    public async Task Enregistrer_MontantATroisDecimales_Refuse()
    {
        var ecran = creerEcran();
        ecran.Libelle = "Alpha";
        ecran.Categorie = "Stock";
        ecran.Montant = 10.123m.ToString(CultureInfo.CurrentCulture);

        await ecran.EnregistrerCommand.ExecuteAsync(null);

        Assert.Contains("décimales", ecran.MessageErreur, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await toutesEnBaseAsync());
    }

    [Fact]
    public async Task Enregistrer_MontantNonNumerique_Refuse()
    {
        var ecran = creerEcran();
        ecran.Libelle = "Alpha";
        ecran.Categorie = "Stock";
        ecran.Montant = "non, mais";

        await ecran.EnregistrerCommand.ExecuteAsync(null);

        Assert.Contains("montant", ecran.MessageErreur, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await toutesEnBaseAsync());
    }

    [Fact]
    public async Task Enregistrer_Creation_LigneEntreEnListeEtEnBase()
    {
        var ecran = creerEcran();
        ecran.Libelle = "  Alpha  ";
        ecran.Categorie = "  Stock  ";
        ecran.Montant = montant(1250.50m);
        ecran.Note = "  note libre  ";

        await ecran.EnregistrerCommand.ExecuteAsync(null);
        await ecran.ChargerAsync();

        var ligne = Assert.Single(ecran.Lignes);
        Assert.Equal("Alpha", ligne.Libelle);
        Assert.Equal("Stock", ligne.Categorie);
        Assert.Equal(1250.50m, ligne.Montant);
        Assert.Equal("note libre", ligne.Note);

        var enBase = await lireEnBaseAsync(ligne.Id);
        Assert.NotNull(enBase);
        Assert.NotEqual(default, enBase.CreatedAt);
        Assert.False(enBase.IsDeleted);
    }

    [Fact]
    public async Task Enregistrer_ApresSelection_ModifieLaLigneEtHorodate()
    {
        var graine = exemple("Alpha", "Stock");
        await semerAsync(graine);

        var ecran = creerEcran();
        await ecran.ChargerAsync();

        ecran.LigneSelectionnee = ecran.Lignes.Single();
        Assert.True(ecran.EstEnEdition);
        Assert.Contains("Modifier", ecran.LibelleFormulaire, StringComparison.Ordinal);

        ecran.Libelle = "Alpha révisé";
        await ecran.EnregistrerCommand.ExecuteAsync(null);
        await ecran.ChargerAsync();

        var ligne = Assert.Single(ecran.Lignes);
        Assert.Equal("Alpha révisé", ligne.Libelle);

        var enBase = await lireEnBaseAsync(graine.Id);
        Assert.Equal("Alpha révisé", enBase!.Libelle);
        Assert.NotNull(enBase.UpdatedAt);
    }

    [Fact]
    public async Task Supprimer_ConvertitLeDeleteEnSuppressionLogique()
    {
        var graine = exemple("Alpha", "Stock");
        await semerAsync(graine);

        var ecran = creerEcran();
        await ecran.ChargerAsync();
        ecran.LigneSelectionnee = ecran.Lignes.Single();

        await ecran.SupprimerCommand.ExecuteAsync(null);
        await ecran.ChargerAsync();

        Assert.Empty(ecran.Lignes);

        // La ligne a survécu : le filtre global du contexte la cache, rien de plus.
        var enBase = await lireEnBaseAsync(graine.Id);
        Assert.NotNull(enBase);
        Assert.True(enBase.IsDeleted);
        Assert.NotNull(enBase.DeletedAt);
    }

    [Fact]
    public async Task Supprimer_SansSelection_Refuse()
    {
        var ecran = creerEcran();

        await ecran.SupprimerCommand.ExecuteAsync(null);

        Assert.Contains("Sélectionnez", ecran.MessageErreur, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Charger_Filtre_RestreintLibelleEtCategorie()
    {
        await semerAsync(exemple("Alpha", "Stock"), exemple("Bêta", "Vente"), exemple("Gamma", "Stock"));

        var ecran = creerEcran();
        await ecran.ChargerAsync();
        Assert.Equal(3, ecran.Lignes.Count);

        ecran.Filtre = "stock";
        await ecran.ChargerAsync();
        Assert.Equal(2, ecran.Lignes.Count);

        ecran.Filtre = "alpha";
        await ecran.RechargerCommand.ExecuteAsync(null);
        Assert.Single(ecran.Lignes);
        Assert.Equal("Alpha", ecran.Lignes[0].Libelle);
    }

    [Fact]
    public async Task Charger_ListeTrieParCategoriePuisLibelle()
    {
        await semerAsync(exemple("Zoulou", "Vente"), exemple("Alpha", "Vente"), exemple("Ressource", "Stock"));

        var ecran = creerEcran();
        await ecran.ChargerAsync();

        Assert.Equal(new[] { "Ressource", "Alpha", "Zoulou" }, ecran.Lignes.Select(l => l.Libelle).ToArray());
    }

    [Fact]
    public async Task Notification_SurNotreTable_RechargeLaListe()
    {
        var ecran = creerEcran();
        await ecran.ChargerAsync();
        Assert.Empty(ecran.Lignes);

        await semerAsync(exemple("Vue d'un autre poste", "Stock"));

        await ecran.ReactualiserSiNotreTableAsync(new SyncNotificationDto
        {
            Table = "Exemples",
            Operation = "I",
            Sender = "framework-autre-poste-4242",
        });

        Assert.Single(ecran.Lignes);
        Assert.Contains("autre poste", ecran.Etat, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Notification_SurAutreTable_LaisseLecranTranquille()
    {
        await semerAsync(exemple("Alpha", "Stock"));

        var ecran = creerEcran();
        await ecran.ChargerAsync();
        var etatAvant = ecran.Etat;

        await ecran.ReactualiserSiNotreTableAsync(new SyncNotificationDto
        {
            Table = "Comptes",
            Operation = "U",
            Sender = "framework-autre-poste-4242",
        });

        Assert.Equal(etatAvant, ecran.Etat);
    }

    [Fact]
    public async Task Notification_PendantUneEcriture_NestPasAppliquee()
    {
        await semerAsync(exemple("Alpha", "Stock"));

        var ecran = creerEcran();
        await ecran.ChargerAsync();

        await semerAsync(exemple("Arrive pendant la frappe", "Vente"));
        ecran.EstOccupe = true;

        await ecran.ReactualiserSiNotreTableAsync(new SyncNotificationDto
        {
            Table = "Exemples",
            Operation = "I",
            Sender = "framework-autre-poste-4242",
        });

        Assert.Single(ecran.Lignes);
    }

    [Fact]
    public async Task Nouveau_VideLeFormulaireEtSortDuModeEdition()
    {
        await semerAsync(exemple("Alpha", "Stock"));

        var ecran = creerEcran();
        await ecran.ChargerAsync();
        ecran.LigneSelectionnee = ecran.Lignes.Single();

        ecran.NouveauCommand.Execute(null);

        Assert.False(ecran.EstEnEdition);
        Assert.Equal("Créer une ligne", ecran.LibelleFormulaire);
        Assert.Equal(string.Empty, ecran.Libelle);
        Assert.Equal(string.Empty, ecran.Categorie);
        Assert.Null(ecran.LigneSelectionnee);
    }

    [Fact]
    public async Task Enregistrer_LigneSupprimeeEntretemps_LeSignale()
    {
        var graine = exemple("Alpha", "Stock");
        await semerAsync(graine);

        var ecran = creerEcran();
        await ecran.ChargerAsync();
        ecran.LigneSelectionnee = ecran.Lignes.Single();

        await using (var contexte = new FrameworkDbContext(_options))
        {
            var cible = await contexte.Exemples.SingleAsync(e => e.Id == graine.Id);
            contexte.Exemples.Remove(cible);
            await contexte.SaveChangesAsync();
        }

        ecran.Libelle = "Alpha revisité";
        await ecran.EnregistrerCommand.ExecuteAsync(null);

        Assert.Contains("disparu", ecran.MessageErreur, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Enregistrer_RoleLecteur_RefuseEtRienECrit()
    {
        var ecran = creerEcran(MatriceParDefaut.Lecteur);
        ecran.Libelle = "Alpha";
        ecran.Categorie = "Stock";

        await ecran.EnregistrerCommand.ExecuteAsync(null);

        Assert.Contains("Droits insuffisants", ecran.MessageErreur, StringComparison.Ordinal);
        Assert.Empty(await toutesEnBaseAsync());
    }

    [Fact]
    public async Task Supprimer_RoleLecteur_RejetEtLaLigneSurvit()
    {
        var graine = exemple("Alpha", "Stock");
        await semerAsync(graine);

        var ecran = creerEcran(MatriceParDefaut.Lecteur);
        await ecran.ChargerAsync();
        ecran.LigneSelectionnee = ecran.Lignes.Single();

        await ecran.SupprimerCommand.ExecuteAsync(null);

        Assert.Contains("Droits insuffisants", ecran.MessageErreur, StringComparison.Ordinal);

        var enBase = await lireEnBaseAsync(graine.Id);
        Assert.NotNull(enBase);
        Assert.False(enBase.IsDeleted);
    }

    [Fact]
    public async Task Charger_HorsSession_AucuneLigneEtLeRefusLeDit()
    {
        await semerAsync(exemple("Alpha", "Stock"));
        _session.FermerSession();

        var ecran = creerEcran();
        await ecran.ChargerAsync();

        Assert.Empty(ecran.Lignes);
        Assert.Contains("Droits insuffisants", ecran.Etat, StringComparison.Ordinal);
        Assert.Contains("(hors session)", ecran.Etat, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Boutons_DependentDuDroitEtNonDeLaSelection()
    {
        await semerAsync(exemple("Alpha", "Stock"));

        var lecteur = creerEcran(MatriceParDefaut.Lecteur);
        await lecteur.ChargerAsync();
        lecteur.LigneSelectionnee = lecteur.Lignes.Single();

        Assert.False(lecteur.PeutEcrire);
        Assert.False(lecteur.PeutSupprimer);
        Assert.False(lecteur.BoutonEnregistrerActif);
        // Lecture seule : le formulaire est bien en édition, seul le droit manque.
        Assert.True(lecteur.EstEnEdition);

        var administrateur = creerEcran(MatriceParDefaut.Administrateur);
        await administrateur.ChargerAsync();
        administrateur.LigneSelectionnee = administrateur.Lignes.Single();

        Assert.True(administrateur.PeutEcrire);
        Assert.True(administrateur.BoutonEnregistrerActif);
        Assert.True(administrateur.BoutonSupprimerActif);
    }

    private sealed class FabriqueContexte : IDbContextFactory<FrameworkDbContext>
    {
        private readonly DbContextOptions<FrameworkDbContext> _options;

        public FabriqueContexte(DbContextOptions<FrameworkDbContext> options) => _options = options;

        public FrameworkDbContext CreateDbContext() => new(_options);
    }

    private sealed class AlertesEnMemoire : IAlertService
    {
        public List<string> Erreurs { get; } = new();

        public void Notify(AlertKind kind, string message)
        {
            if (kind == AlertKind.Error)
            {
                Erreurs.Add(message);
            }
        }
    }
}
