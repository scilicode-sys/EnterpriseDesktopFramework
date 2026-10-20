using Framework.Core.Data;
using Framework.Core.Notifications;
using Framework.Core.Rbac;
using Framework.Core.Security;
using Framework.Core.Sync;
using Framework.Data;
using Framework.Data.Entities;
using Framework.WPF.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Framework.Tests;

/// <summary>
/// Écran « Comptes » (P1 / EF-11) : créer un compte sans SQL, changer un rôle,
/// désactiver, dégeler, supprimer logiquement — et les trois gardes qui empêchent
/// l'écran de verrouiller le déploiement (propre rôle, propre compte, rôle hors
/// matrice). EF InMemory suffit, comme pour les autres écrans.
/// </summary>
public sealed class ComptesViewModelTests : IDisposable
{
    private readonly DbContextOptions<FrameworkDbContext> _options;
    private readonly ServiceProvider _portes;
    private readonly MatriceRoles _matrice = MatriceParDefaut.Creer();
    private readonly AlertesEnMemoire _alertes = new();
    private readonly FakeSession _session = new(MatriceParDefaut.Administrateur);

    public ComptesViewModelTests()
    {
        _options = new DbContextOptionsBuilder<FrameworkDbContext>()
            .UseInMemoryDatabase($"framework-comptes-{Guid.NewGuid():N}")
            // L'audit n'est plus dans le contexte : un poste le déclare via
            // AddFrameworkData, un test le déclare ici.
            .AddInterceptors(new AuditSaveChangesInterceptor(_session, new FakeHorloge()))
            .Options;

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<FrameworkDbContext>>(new FabriqueContexte(_options));
        services.AddTransient<IUnitOfWork, UnitOfWork>();
        _portes = services.BuildServiceProvider();
    }

    public void Dispose() => _portes.Dispose();

    /// <summary>Session Administrateur par défaut ; les refus se testent en changeant le rôle.</summary>
    private ComptesViewModel creerEcran(string? role = null)
    {
        if (role is not null)
        {
            _session.SeConnecter(role);
        }

        return new ComptesViewModel(
            _portes.GetRequiredService<IServiceScopeFactory>(),
            _alertes,
            new AutorisationService(_session, _matrice),
            _session,
            _matrice);
    }

    private async Task semerAsync(params CompteEntity[] entites)
    {
        await using var contexte = new FrameworkDbContext(_options);
        contexte.Comptes.AddRange(entites);
        await contexte.SaveChangesAsync();
    }

    private async Task<CompteEntity?> lireEnBaseAsync(Guid id)
    {
        await using var contexte = new FrameworkDbContext(_options);
        return await contexte.Comptes.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
    }

    private async Task<List<CompteEntity>> tousEnBaseAsync()
    {
        await using var contexte = new FrameworkDbContext(_options);
        return await contexte.Comptes.IgnoreQueryFilters().ToListAsync();
    }

    private static CompteEntity compte(
        string username,
        string role = MatriceParDefaut.Lecteur,
        string motDePasse = "motdepasse",
        bool actif = true)
        => new()
        {
            Username = username,
            NomComplet = $"Titulaire {username}",
            Role = role,
            PasswordHash = PasswordHasher.Hash(motDePasse),
            Actif = actif,
        };

    /// <summary>Le formulaire prêt à créer, avec le strict minimum valable.</summary>
    private static void remplirCreation(
        ComptesViewModel ecran,
        string username,
        string role = MatriceParDefaut.Lecteur,
        string motDePasse = "motdepasse")
    {
        ecran.Identifiant = username;
        ecran.NomComplet = "Marie Dupont";
        ecran.RoleSelectionne = role;
        ecran.MotDePasse = motDePasse;
        ecran.Confirmation = motDePasse;
    }

    [Fact]
    public async Task Enregistrer_Creation_LeHashSeulEntreEnBase()
    {
        var ecran = creerEcran();
        ecran.Identifiant = "  agent2 ";
        ecran.NomComplet = "  Marie Dupont ";
        ecran.RoleSelectionne = MatriceParDefaut.Lecteur;
        ecran.MotDePasse = "motdepasse";
        ecran.Confirmation = "motdepasse";

        await ecran.EnregistrerCommand.ExecuteAsync(null);
        await ecran.ChargerAsync();

        var ligne = Assert.Single(ecran.Lignes);
        Assert.Equal("agent2", ligne.Username);
        Assert.Equal("Marie Dupont", ligne.NomComplet);
        Assert.Equal(MatriceParDefaut.Lecteur, ligne.Role);
        Assert.True(ligne.Actif);

        var enBase = await lireEnBaseAsync(ligne.Id);
        Assert.NotNull(enBase);
        Assert.StartsWith("pbkdf2$sha256$", enBase.PasswordHash, StringComparison.Ordinal);
        Assert.DoesNotContain("motdepasse", enBase.PasswordHash, StringComparison.Ordinal);
        Assert.True(PasswordHasher.Verify("motdepasse", enBase.PasswordHash));
    }

    [Fact]
    public async Task Etat_GardeLaTraceDeLOpérationAprèsLeRechargement()
    {
        var ecran = creerEcran();
        remplirCreation(ecran, "agent2");

        await ecran.EnregistrerCommand.ExecuteAsync(null);

        // La liste s'est rechargée en finally : l'état doit pourtant dire ce qui
        // vient d'être fait, sinon l'écran ne rapporte plus rien à l'exploitant.
        Assert.Contains("créé avec le rôle", ecran.Etat, StringComparison.Ordinal);
        Assert.Single(ecran.Lignes);
    }

    [Fact]
    public async Task Enregistrer_SansMotDePasseALaCreation_Refuse()    {
        var ecran = creerEcran();
        remplirCreation(ecran, "agent2", motDePasse: string.Empty);

        await ecran.EnregistrerCommand.ExecuteAsync(null);

        Assert.Contains("mot de passe est obligatoire", ecran.MessageErreur, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await tousEnBaseAsync());
    }

    [Fact]
    public async Task Enregistrer_MotDePasseTropCourt_Refuse()
    {
        var ecran = creerEcran();
        remplirCreation(ecran, "agent2", motDePasse: "abc1234");

        await ecran.EnregistrerCommand.ExecuteAsync(null);

        Assert.Contains("au moins 8 caractères", ecran.MessageErreur, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await tousEnBaseAsync());
    }

    [Fact]
    public async Task Enregistrer_ConfirmationDifferente_Refuse()
    {
        var ecran = creerEcran();
        remplirCreation(ecran, "agent2");
        ecran.Confirmation = "motdepassx";

        await ecran.EnregistrerCommand.ExecuteAsync(null);

        Assert.Contains("confirmation", ecran.MessageErreur, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await tousEnBaseAsync());
    }

    [Fact]
    public async Task Enregistrer_IdentifiantAvecEspace_Refuse()
    {
        var ecran = creerEcran();
        remplirCreation(ecran, "agent deux");

        await ecran.EnregistrerCommand.ExecuteAsync(null);

        Assert.Contains("d'espace", ecran.MessageErreur, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await tousEnBaseAsync());
    }

    [Fact]
    public async Task Enregistrer_RoleNonDeclareDansLaMatrice_Refuse()
    {
        var ecran = creerEcran();
        remplirCreation(ecran, "agent2", role: "Comptable");

        await ecran.EnregistrerCommand.ExecuteAsync(null);

        Assert.Contains("pas déclaré dans la matrice", ecran.MessageErreur, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await tousEnBaseAsync());
    }

    [Fact]
    public async Task Enregistrer_IdentifiantDejaPorte_Refuse()
    {
        var graine = compte("agent2");
        await semerAsync(graine);

        var ecran = creerEcran();
        remplirCreation(ecran, "agent2");

        await ecran.EnregistrerCommand.ExecuteAsync(null);

        Assert.Contains("déjà porté", ecran.MessageErreur, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await tousEnBaseAsync());
    }

    [Fact]
    public async Task Enregistrer_SansMotDePasseEnModification_ConserveLeHash()
    {
        var graine = compte("agent2");
        await semerAsync(graine);

        var ecran = creerEcran();
        await ecran.ChargerAsync();
        ecran.LigneSelectionnee = ecran.Lignes.Single();

        ecran.NomComplet = "Marie D.";
        await ecran.EnregistrerCommand.ExecuteAsync(null);

        var enBase = await lireEnBaseAsync(graine.Id);
        Assert.NotNull(enBase);
        Assert.Equal("Marie D.", enBase.NomComplet);
        Assert.Equal(graine.PasswordHash, enBase.PasswordHash);
    }

    [Fact]
    public async Task Enregistrer_NouveauMotDePasse_RechangeLeHashEtLeveLeGel()
    {
        var graine = compte("agent2");
        graine.EchecsConsecutifs = 2;
        graine.GelJusqua = DateTime.Now.AddMinutes(1);
        await semerAsync(graine);

        var ecran = creerEcran();
        await ecran.ChargerAsync();
        ecran.LigneSelectionnee = ecran.Lignes.Single();

        ecran.MotDePasse = "nouveauMotDePasse";
        ecran.Confirmation = "nouveauMotDePasse";
        await ecran.EnregistrerCommand.ExecuteAsync(null);

        var enBase = await lireEnBaseAsync(graine.Id);
        Assert.NotNull(enBase);
        Assert.NotEqual(graine.PasswordHash, enBase.PasswordHash);
        Assert.True(PasswordHasher.Verify("nouveauMotDePasse", enBase.PasswordHash));
        Assert.Equal(0, enBase.EchecsConsecutifs);
        Assert.Null(enBase.GelJusqua);
    }

    [Fact]
    public async Task Enregistrer_ChangerSonPropreRole_Refuse()
    {
        var soi = compte("moi", MatriceParDefaut.Administrateur);
        soi.Id = _session.CurrentUser!.Id;
        await semerAsync(soi, compte("agent2"));

        var ecran = creerEcran();
        await ecran.ChargerAsync();
        ecran.LigneSelectionnee = ecran.Lignes.Single(l => l.EstCompteCourant);

        ecran.RoleSelectionne = MatriceParDefaut.Lecteur;
        await ecran.EnregistrerCommand.ExecuteAsync(null);

        Assert.Contains("votre propre rôle", ecran.MessageErreur, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(MatriceParDefaut.Administrateur, (await lireEnBaseAsync(soi.Id))!.Role);
    }

    [Fact]
    public async Task BasculerActif_DesactiveLeCompteEnBase()
    {
        var graine = compte("agent2");
        await semerAsync(graine);

        var ecran = creerEcran();
        await ecran.ChargerAsync();
        ecran.LigneSelectionnee = ecran.Lignes.Single();

        await ecran.BasculerActifCommand.ExecuteAsync(null);

        Assert.Contains("désactivé", ecran.Etat, StringComparison.OrdinalIgnoreCase);
        Assert.False((await lireEnBaseAsync(graine.Id))!.Actif);
    }

    [Fact]
    public async Task BasculerActif_LeCompteDeLaSessionEstProtege()
    {
        var soi = compte("moi", MatriceParDefaut.Administrateur);
        soi.Id = _session.CurrentUser!.Id;
        await semerAsync(soi);

        var ecran = creerEcran();
        await ecran.ChargerAsync();
        ecran.LigneSelectionnee = ecran.Lignes.Single();

        await ecran.BasculerActifCommand.ExecuteAsync(null);

        Assert.Contains("ce poste tient la session", ecran.MessageErreur, StringComparison.Ordinal);
        Assert.True((await lireEnBaseAsync(soi.Id))!.Actif);
    }

    [Fact]
    public async Task Degeler_EffaceLeGelEtLesEchecs()
    {
        var graine = compte("agent2");
        graine.EchecsConsecutifs = 3;
        graine.GelJusqua = DateTime.Now.AddSeconds(45);
        await semerAsync(graine);

        var ecran = creerEcran();
        await ecran.ChargerAsync();
        ecran.LigneSelectionnee = ecran.Lignes.Single();

        Assert.Contains("Gel jusqu'à", ecran.LigneSelectionnee.Securite, StringComparison.Ordinal);
        Assert.True(ecran.BoutonDegelerActif);

        await ecran.DegelerCommand.ExecuteAsync(null);

        var enBase = await lireEnBaseAsync(graine.Id);
        Assert.NotNull(enBase);
        Assert.Null(enBase.GelJusqua);
        Assert.Equal(0, enBase.EchecsConsecutifs);
    }

    [Fact]
    public async Task Supprimer_ConvertitLeDeleteEnSuppressionLogique()
    {
        var graine = compte("agent2");
        await semerAsync(graine);

        var ecran = creerEcran();
        await ecran.ChargerAsync();
        ecran.LigneSelectionnee = ecran.Lignes.Single();

        await ecran.SupprimerCommand.ExecuteAsync(null);
        await ecran.ChargerAsync();

        Assert.Empty(ecran.Lignes);

        var enBase = await lireEnBaseAsync(graine.Id);
        Assert.NotNull(enBase);
        Assert.True(enBase.IsDeleted);
        Assert.NotNull(enBase.DeletedAt);
    }

    [Fact]
    public async Task Supprimer_LeCompteDeLaSessionEstProtege()
    {
        var soi = compte("moi", MatriceParDefaut.Administrateur);
        soi.Id = _session.CurrentUser!.Id;
        await semerAsync(soi);

        var ecran = creerEcran();
        await ecran.ChargerAsync();
        ecran.LigneSelectionnee = ecran.Lignes.Single();

        await ecran.SupprimerCommand.ExecuteAsync(null);

        Assert.Contains("ce poste tient la session", ecran.MessageErreur, StringComparison.Ordinal);

        var enBase = await lireEnBaseAsync(soi.Id);
        Assert.NotNull(enBase);
        Assert.False(enBase.IsDeleted);
    }

    [Fact]
    public async Task RoleUtilisateur_ListeVisibleAucuneEcriture()
    {
        await semerAsync(compte("agent2"));

        var ecran = creerEcran(MatriceParDefaut.Utilisateur);
        await ecran.ChargerAsync();

        Assert.Single(ecran.Lignes);
        Assert.False(ecran.PeutGerer);
        Assert.False(ecran.BoutonEnregistrerActif);

        remplirCreation(ecran, "agent3");
        await ecran.EnregistrerCommand.ExecuteAsync(null);

        Assert.Contains("Droits insuffisants", ecran.MessageErreur, StringComparison.Ordinal);
        Assert.Single(await tousEnBaseAsync());
    }

    [Fact]
    public async Task RoleLecteur_LesComptesNeSontMemePasListes()
    {
        await semerAsync(compte("agent2"));

        var ecran = creerEcran(MatriceParDefaut.Lecteur);
        await ecran.ChargerAsync();

        Assert.Empty(ecran.Lignes);
        Assert.Contains("Droits insuffisants", ecran.Etat, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Charger_HorsSession_LeRefusLeDit()
    {
        await semerAsync(compte("agent2"));
        _session.FermerSession();

        var ecran = creerEcran();
        await ecran.ChargerAsync();

        Assert.Empty(ecran.Lignes);
        Assert.Contains("(hors session)", ecran.Etat, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Charger_Filtre_RestreintIdentifiantNomEtRole()
    {
        await semerAsync(
            compte("alpha", MatriceParDefaut.Lecteur),
            compte("bravo", MatriceParDefaut.Utilisateur),
            compte("charlie", MatriceParDefaut.Lecteur));

        var ecran = creerEcran();
        await ecran.ChargerAsync();
        Assert.Equal(3, ecran.Lignes.Count);

        ecran.Filtre = "lecteur";
        await ecran.ChargerAsync();
        Assert.Equal(new[] { "alpha", "charlie" }, ecran.Lignes.Select(l => l.Username).ToArray());

        ecran.Filtre = "bravo";
        await ecran.RechargerCommand.ExecuteAsync(null);
        Assert.Single(ecran.Lignes);
    }

    [Fact]
    public async Task Selection_LeFormulaireSeRemplitSansLeMotDePasse()
    {
        var graine = compte("agent2");
        await semerAsync(graine);

        var ecran = creerEcran();
        await ecran.ChargerAsync();

        var generationAvant = ecran.GenerationMotDePasse;
        ecran.LigneSelectionnee = ecran.Lignes.Single();

        Assert.True(ecran.EstEnEdition);
        Assert.Equal("agent2", ecran.Identifiant);
        Assert.Equal(string.Empty, ecran.MotDePasse);
        Assert.Equal(generationAvant + 1, ecran.GenerationMotDePasse);
        Assert.Contains("conserver le mot de passe actuel", ecran.ConsigneMotDePasse, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nouveau_VideLeFormulaireEtPasseEnCreation()
    {
        await semerAsync(compte("agent2"));

        var ecran = creerEcran();
        await ecran.ChargerAsync();
        ecran.LigneSelectionnee = ecran.Lignes.Single();

        ecran.NouveauCommand.Execute(null);

        Assert.False(ecran.EstEnEdition);
        Assert.Equal("Créer un compte", ecran.LibelleFormulaire);
        Assert.Equal(string.Empty, ecran.Identifiant);
        Assert.Null(ecran.LigneSelectionnee);
    }

    [Fact]
    public async Task Notification_SurComptes_RechargeLaListe()
    {
        var ecran = creerEcran();
        await ecran.ChargerAsync();
        Assert.Empty(ecran.Lignes);

        await semerAsync(compte("Vu depuis un autre poste"));

        await ecran.ReactualiserSiNotreTableAsync(new SyncNotificationDto
        {
            Table = "Comptes",
            Operation = "I",
            Sender = "framework-autre-poste-4242",
        });

        Assert.Single(ecran.Lignes);
        Assert.Contains("autre poste", ecran.Etat, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Notification_SurAutreTable_LaisseLecranTranquille()
    {
        await semerAsync(compte("agent2"));

        var ecran = creerEcran();
        await ecran.ChargerAsync();
        var etatAvant = ecran.Etat;

        await ecran.ReactualiserSiNotreTableAsync(new SyncNotificationDto
        {
            Table = "Exemples",
            Operation = "U",
            Sender = "framework-autre-poste-4242",
        });

        Assert.Equal(etatAvant, ecran.Etat);
    }

    [Fact]
    public async Task Roles_Proposes_SontCeuxDeLaMatrice()
    {
        var ecran = creerEcran();

        // Administrateur en tête (décision d'affichage), puis l'ordre alphabétique local.
        Assert.Equal(
            new[] { MatriceParDefaut.Administrateur, MatriceParDefaut.Lecteur, MatriceParDefaut.Utilisateur },
            ecran.Roles.ToArray());
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
