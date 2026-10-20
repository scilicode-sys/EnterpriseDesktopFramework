using Framework.Core.Data;
using Framework.Core.Rbac;
using Framework.Data;
using Framework.Data.Entities;
using Framework.WPF.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Framework.Tests;

/// <summary>
/// Écran de contrôle du support : ce que la matrice déclare, ce que la session
/// tient, les deux ratés qu'il doit crier — et qui porte chaque rôle sur ce
/// poste. La liste des comptes se lit comme sur les autres écrans, sous garde
/// de « comptes.consulter ».
/// </summary>
public sealed class DroitsViewModelTests : IDisposable
{
    private readonly DbContextOptions<FrameworkDbContext> _options;
    private readonly ServiceProvider _portes;
    private readonly MatriceRoles _matrice = MatriceParDefaut.Creer();

    public DroitsViewModelTests()
    {
        _options = new DbContextOptionsBuilder<FrameworkDbContext>()
            .UseInMemoryDatabase($"framework-droits-{Guid.NewGuid():N}")
            .Options;

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<FrameworkDbContext>>(new FabriqueContexte(_options));
        services.AddTransient<IUnitOfWork, UnitOfWork>();
        _portes = services.BuildServiceProvider();
    }

    public void Dispose() => _portes.Dispose();

    private DroitsViewModel creer(string? role, IMatriceRoles? matrice = null)
    {
        var session = new FakeSession(role);
        var grille = matrice ?? _matrice;
        return new DroitsViewModel(
            session,
            grille,
            new AutorisationService(session, grille),
            _portes.GetRequiredService<IServiceScopeFactory>());
    }

    private async Task semerAsync(params (string Username, string Role, bool Actif)[] comptes)
    {
        await using var contexte = new FrameworkDbContext(_options);
        contexte.Comptes.AddRange(comptes.Select(c => new CompteEntity
        {
            Username = c.Username,
            NomComplet = $"Titulaire {c.Username}",
            Role = c.Role,
            PasswordHash = "pbkdf2$sha256$depuis-le-test",
            Actif = c.Actif,
        }));
        await contexte.SaveChangesAsync();
    }

    [Fact]
    public async Task Charger_HorsSession_RoleVideEtRefusAffiche()
    {
        var ecran = creer(null);
        await ecran.ChargerAsync();

        Assert.Empty(ecran.Roles);
        Assert.Equal("(hors session)", ecran.SessionCourante);
        Assert.Contains("Accès refusé", ecran.Avertissement, StringComparison.Ordinal);
        Assert.True(ecran.EstAvertissementVisible);
    }

    [Fact]
    public async Task Charger_RoleNonDeclare_LeRefusEstLeMemeEtRienNEstFuite()
    {
        var ecran = creer("Comptable");
        await ecran.ChargerAsync();

        Assert.Empty(ecran.Roles);
        Assert.Contains("droits.consulter", ecran.Avertissement, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Charger_Lecteur_DerouleLaMatriceEtMarqueLaSession()
    {
        var ecran = creer(MatriceParDefaut.Lecteur);
        await ecran.ChargerAsync();

        Assert.Equal(new[] { "Administrateur", "Lecteur", "Utilisateur" }, ecran.Roles.Select(r => r.Cle).ToArray());
        Assert.Equal("Agent Test — rôle « Lecteur »", ecran.SessionCourante);
        Assert.False(ecran.EstAvertissementVisible);

        var courant = Assert.Single(ecran.Roles.Where(r => r.EstRoleCourant));
        Assert.Equal(MatriceParDefaut.Lecteur, courant.Cle);
        Assert.Equal("aucun", courant.HeriteDe);

        Assert.Equal("Lecteur", ecran.Roles.Single(r => r.Cle == "Utilisateur").HeriteDe);
    }

    [Fact]
    public async Task Charger_Administrateur_LaSauvageonneEstDiteSansEtreListee()
    {
        var ecran = creer(MatriceParDefaut.Administrateur);
        await ecran.ChargerAsync();

        var admin = ecran.Roles.Single(r => r.Cle == MatriceParDefaut.Administrateur);

        Assert.Equal(new[] { Droits.Tout }, admin.Permissions);
        Assert.Equal("toutes permissions", admin.Compte);
        Assert.Contains("« * »", admin.Resume, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Charger_Utilisateur_HeriteCompteLesPermissionsDuDessous()
    {
        var ecran = creer(MatriceParDefaut.Utilisateur);
        await ecran.ChargerAsync();

        var utilisateur = ecran.Roles.Single(r => r.Cle == MatriceParDefaut.Utilisateur);

        // Les 9 permissions propres + droits.consulter venu de Lecteur.
        Assert.Equal(10, utilisateur.Permissions.Count);
        Assert.Contains(Droits.DroitsConsulter, utilisateur.Permissions);
        Assert.Equal("10 permissions", utilisateur.Compte);
        Assert.Contains(" · ", utilisateur.Resume, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Charger_RoleDeclareSansDroit_LeSignaleCommeImpasse()
    {
        var matrice = new MatriceRoles(new[]
        {
            new RoleDefini(MatriceParDefaut.Administrateur, "Administrateur", new[] { Droits.Tout }),
            new RoleDefini("Stagiaire", "Stagiaire", RoleDefini.Aucune),
            new RoleDefini("Assistant", "Assistant", RoleDefini.Aucune),
        });

        var ecran = creer(MatriceParDefaut.Administrateur, matrice);
        await ecran.ChargerAsync();

        Assert.Contains("« Stagiaire », « Assistant »", ecran.Avertissement, StringComparison.Ordinal);
        Assert.Contains("ne donnent accès", ecran.Avertissement, StringComparison.Ordinal);
        Assert.True(ecran.EstAvertissementVisible);

        var stagiaire = ecran.Roles.Single(r => r.Cle == "Stagiaire");
        Assert.Equal("aucune permission", stagiaire.Compte);
        Assert.Equal("—", stagiaire.Resume);
    }

    [Fact]
    public async Task Recharger_ApresUnChangementDeSession_ReposeLaSessionCourante()
    {
        var session = new FakeSession(MatriceParDefaut.Lecteur);
        var ecran = new DroitsViewModel(
            session, _matrice, new AutorisationService(session, _matrice),
            _portes.GetRequiredService<IServiceScopeFactory>());

        await ecran.ChargerAsync();
        Assert.DoesNotContain(ecran.Roles, r => r.EstRoleCourant && r.Cle == MatriceParDefaut.Administrateur);

        session.SeConnecter(MatriceParDefaut.Administrateur);
        await ecran.RechargerCommand.ExecuteAsync(null);

        Assert.Equal("Agent Test — rôle « Administrateur »", ecran.SessionCourante);
        Assert.True(ecran.Roles.Single(r => r.Cle == MatriceParDefaut.Administrateur).EstRoleCourant);
    }

    [Fact]
    public async Task Recharger_Nouvelle_Roles_QueLaVueEstAvertie()
    {
        // Le bouton « Recharger » ne sert à rien si la vue n'apprend pas que la
        // liste a changé : c'est le repeindre, pas seulement recalculer en mémoire.
        var session = new FakeSession(MatriceParDefaut.Lecteur);
        var ecran = new DroitsViewModel(
            session, _matrice, new AutorisationService(session, _matrice),
            _portes.GetRequiredService<IServiceScopeFactory>());
        var notifiees = new List<string?>();
        ecran.PropertyChanged += (_, e) => notifiees.Add(e.PropertyName);

        await ecran.ChargerAsync();
        session.SeConnecter(MatriceParDefaut.Administrateur);
        await ecran.RechargerCommand.ExecuteAsync(null);

        Assert.Contains(nameof(DroitsViewModel.Roles), notifiees);
    }

    [Fact]
    public async Task Charger_ComptesPresentes_DitQuiPorteChaqueRole()
    {
        await semerAsync(
            ("amine", MatriceParDefaut.Administrateur, true),
            ("stage", MatriceParDefaut.Lecteur, true),
            ("comptable1", MatriceParDefaut.Utilisateur, true));

        var ecran = creer(MatriceParDefaut.Administrateur);
        await ecran.ChargerAsync();

        Assert.Equal("1 compte : stage", ecran.Roles.Single(r => r.Cle == MatriceParDefaut.Lecteur).Porteurs);
        Assert.Equal("1 compte : amine", ecran.Roles.Single(r => r.Cle == MatriceParDefaut.Administrateur).Porteurs);
        Assert.Empty(ecran.NoteComptes);
    }

    [Fact]
    public async Task Charger_RoleSansCompte_LeDitSansInventer()
    {
        await semerAsync(("amine", MatriceParDefaut.Administrateur, true));

        var ecran = creer(MatriceParDefaut.Administrateur);
        await ecran.ChargerAsync();

        Assert.Equal(0, ecran.Roles.Single(r => r.Cle == MatriceParDefaut.Lecteur).NbComptes);
        Assert.Equal("aucun compte ne porte ce rôle", ecran.Roles.Single(r => r.Cle == MatriceParDefaut.Lecteur).Porteurs);
    }

    [Fact]
    public async Task Charger_CompteInactif_LeSignaleSurSaLigne()
    {
        await semerAsync(
            ("stage", MatriceParDefaut.Lecteur, false),
            ("accueil", MatriceParDefaut.Lecteur, true));

        var ecran = creer(MatriceParDefaut.Administrateur);
        await ecran.ChargerAsync();

        Assert.Equal("2 comptes : accueil, stage (désactivé)",
            ecran.Roles.Single(r => r.Cle == MatriceParDefaut.Lecteur).Porteurs);
    }

    [Fact]
    public async Task Charger_SansDroitComptes_DitQuIlNaPasCompte()
    {
        // Un rôle qui tient droits.consulter mais pas comptes.consulter doit voir
        // la matrice entière et la mention explicite qu'aucun compte n'a été lu.
        await semerAsync(("stage", MatriceParDefaut.Lecteur, true));

        var matrice = new MatriceRoles(new[]
        {
            new RoleDefini(MatriceParDefaut.Administrateur, "Administrateur", new[] { Droits.Tout }),
            new RoleDefini("Support", "Support", new[] { Droits.DroitsConsulter }),
        });

        var ecran = creer("Support", matrice);
        await ecran.ChargerAsync();

        Assert.Contains("comptes.consulter", ecran.NoteComptes, StringComparison.Ordinal);
        Assert.True(ecran.EstNoteVisible);
        Assert.Equal("comptes non listés ici", ecran.Roles.Single(r => r.Cle == "Support").Porteurs);
        Assert.Null(ecran.Roles.Single(r => r.Cle == "Support").NbComptes);
    }

    [Fact]
    public async Task Charger_BeaucoupDeComptes_TroneLaListeSansFausserLeNombre()
    {
        await semerAsync(
            ("c1", MatriceParDefaut.Utilisateur, true),
            ("c2", MatriceParDefaut.Utilisateur, true),
            ("c3", MatriceParDefaut.Utilisateur, true),
            ("c4", MatriceParDefaut.Utilisateur, true),
            ("c5", MatriceParDefaut.Utilisateur, true),
            ("c6", MatriceParDefaut.Utilisateur, true),
            ("c7", MatriceParDefaut.Utilisateur, true),
            ("c8", MatriceParDefaut.Utilisateur, true));

        var ecran = creer(MatriceParDefaut.Administrateur);
        await ecran.ChargerAsync();

        var porteurs = ecran.Roles.Single(r => r.Cle == MatriceParDefaut.Utilisateur).Porteurs;
        Assert.Equal("8 comptes : c1, c2, c3, c4, c5, c6 … 2 autres", porteurs);
    }

    private sealed class FabriqueContexte : IDbContextFactory<FrameworkDbContext>
    {
        private readonly DbContextOptions<FrameworkDbContext> _options;

        public FabriqueContexte(DbContextOptions<FrameworkDbContext> options) => _options = options;

        public FrameworkDbContext CreateDbContext() => new(_options);
    }
}
