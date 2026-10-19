using Framework.Core.Auth;
using Framework.Data;
using Framework.Data.Entities;
using Framework.Data.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Framework.Tests;

/// <summary>
/// Session et anti-force-brute du service d'authentification, sur la brique
/// InMemory d'EF — les SQLSTATE et le Npgsql restent l'affaire des postes.
/// Le temps est piloté par FakeHorloge : les 60 secondes de gel se testent
/// en avançant l'horloge, jamais en dormant.
/// </summary>
public class AuthServiceTests : IDisposable
{
    private readonly DbContextOptions<FrameworkDbContext> _options;
    private readonly FakeHorloge _horloge = new();

    public AuthServiceTests()
    {
        _options = new DbContextOptionsBuilder<FrameworkDbContext>()
            .UseInMemoryDatabase($"framework-auth-{Guid.NewGuid():N}")
            .Options;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private AuthService CreerService()
        => new(new FabriqueContexte(_options), _horloge, NullLogger<AuthService>.Instance);

    private sealed class FabriqueContexte : IDbContextFactory<FrameworkDbContext>
    {
        private readonly DbContextOptions<FrameworkDbContext> _options;

        public FabriqueContexte(DbContextOptions<FrameworkDbContext> options) => _options = options;

        public FrameworkDbContext CreateDbContext() => new(_options);
    }

    private async Task SemerCompteAsync(
        string username = "amara",
        string password = "secret!123",
        bool actif = true)
    {
        await using var contexte = new FrameworkDbContext(_options);
        contexte.Comptes.Add(new CompteEntity
        {
            Username = username,
            NomComplet = "Amara Diallo",
            Role = "Administrateur",
            PasswordHash = Framework.Core.Security.PasswordHasher.Hash(password),
            Actif = actif,
        });
        await contexte.SaveChangesAsync();
    }

    [Fact]
    public async Task Login_BonsIdentifiants_OuvreLaSessionEtPublieLÉtat()
    {
        await SemerCompteAsync();
        var service = CreerService();

        AppUser? publie = null;
        service.AuthenticationStateChanged += (_, utilisateur) => publie = utilisateur;

        var resultat = await service.LoginAsync("amara", "secret!123");

        Assert.True(resultat.IsSuccess);
        Assert.True(service.IsAuthenticated);
        Assert.NotNull(service.CurrentUser);
        Assert.Equal("Administrateur", service.CurrentUser!.Role);
        Assert.Equal("Amara Diallo", publie?.NomComplet);
    }

    [Fact]
    public async Task Login_Inconnu_MêmeMessageQueMauvaisMotDePasse()
    {
        var service = CreerService();

        var intrus = await service.LoginAsync("intrus", "peu-importe");
        await SemerCompteAsync();
        var fauxMdp = await service.LoginAsync("amara", "faux");

        Assert.Equal(AuthenticationStatus.WrongCredentials, intrus.Status);
        Assert.Equal(fauxMdp.Message, intrus.Message);
    }

    [Fact]
    public async Task Login_CompteDésactivé_RefusSansToucherAuGel()
    {
        await SemerCompteAsync(actif: false);
        var service = CreerService();

        var resultat = await service.LoginAsync("amara", "secret!123");

        Assert.Equal(AuthenticationStatus.Deactivated, resultat.Status);
    }

    [Fact]
    public async Task TroisÉchecs_PuisQuatrième_GèleLeCompteSoixanteSecondes()
    {
        await SemerCompteAsync();

        var attaquant = CreerService();

        for (var i = 0; i < AuthService.MaxEchecsAvantBlocage; i++)
        {
            var tentative = await attaquant.LoginAsync("amara", "force-brute");
            Assert.Equal(AuthenticationStatus.WrongCredentials, tentative.Status);
        }

        var givré = await attaquant.LoginAsync("amara", "secret!123");
        Assert.Equal(AuthenticationStatus.Locked, givré.Status);

        // Le bon mot de passe lui-même ne force plus rien pendant le gel.
        _horloge.Maintenant = _horloge.Maintenant.AddSeconds(61);

        var aprèsGel = await attaquant.LoginAsync("amara", "secret!123");
        Assert.True(aprèsGel.IsSuccess);
    }

    [Fact]
    public async Task Réussite_EffaceLeCompteurDEchecs()
    {
        await SemerCompteAsync();

        var service = CreerService();
        await service.LoginAsync("amara", "faux");
        await service.LoginAsync("amara", "faux");
        await service.LoginAsync("amara", "secret!123");

        await using var contexte = new FrameworkDbContext(_options);
        var compte = await contexte.Comptes.SingleAsync(c => c.Username == "amara");

        Assert.Equal(0, compte.EchecsConsecutifs);
        Assert.Null(compte.GelJusqua);
    }

    [Fact]
    public async Task Logout_FermeLaSessionEtPublieNul()
    {
        await SemerCompteAsync();
        var service = CreerService();
        await service.LoginAsync("amara", "secret!123");

        var publie = true;
        service.AuthenticationStateChanged += (_, utilisateur) => publie = utilisateur is not null;
        await service.LogoutAsync();

        Assert.False(service.IsAuthenticated);
        Assert.Null(service.CurrentUser);
        Assert.False(publie);
    }

    [Fact]
    public async Task PremierCompte_SemencePuisPorteFermée()
    {
        var service = CreerService();

        Assert.False(await service.HasAnyAccountAsync());

        var amorce = await service.CreerPremierCompteAsync("premier", "Premier Post", "Administrateur", "P@sse!2026");
        Assert.True(amorce.IsSuccess);
        Assert.True(await service.HasAnyAccountAsync());

        var rejeu = await service.CreerPremierCompteAsync("autre", "Autre", "Employé", "P@sse!2026");
        Assert.False(rejeu.IsSuccess);
        Assert.Equal(AuthenticationStatus.TechnicalFailure, rejeu.Status);

        // Le hash stocké est bien au format PBKDF2 du framework.
        await using var contexte = new FrameworkDbContext(_options);
        var compte = await contexte.Comptes.SingleAsync(c => c.Username == "premier");
        Assert.StartsWith("pbkdf2$sha256$", compte.PasswordHash);
        Assert.True(Framework.Core.Security.PasswordHasher.Verify("P@sse!2026", compte.PasswordHash));
    }
}
