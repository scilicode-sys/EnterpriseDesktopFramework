using Framework.Core.Auth;
using Framework.Core.Notifications;
using Framework.Core.Rbac;
using Framework.Core.Theming;
using Framework.Core.Time;
using Framework.WPF.Services;
using Framework.WPF.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Framework.Tests;

/// <summary>
/// La barre latérale est la seule interface de navigation du shell : elle doit
/// se rouvrir et se refermer avec la session, sans redémarrage — le shell est
/// singleton, un poste qui change de personne ne doit pas garder les écrans
/// écrits de la précédente.
/// </summary>
public class ShellViewModelTests
{
    private static readonly Type TableauDeBord = typeof(DashboardViewModel);

    private static (ShellViewModel shell, FakeSession session, NavigationService navigation) creer(
        IMatriceRoles? matrice = null,
        string? role = null)
    {
        var grille = matrice ?? MatriceParDefaut.Creer();
        var session = new FakeSession(role);
        var autorisation = new AutorisationService(session, grille);

        var services = new ServiceCollection();
        services.AddSingleton<IHorloge, FakeHorloge>();
        services.AddSingleton(grille);
        services.AddSingleton<IAuthService>(session);
        services.AddSingleton<IAutorisationService>(autorisation);
        services.AddSingleton<IAlertService, AlertesSilencieuses>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<DroitsViewModel>();
        services.AddTransient<ExempleViewModel>();

        var provider = services.BuildServiceProvider();
        var navigation = new NavigationService(provider);
        var shell = new ShellViewModel(navigation, new ThemeEnMemoire(), session, autorisation);

        return (shell, session, navigation);
    }

    [Fact]
    public void HorsSession_SeulLeTableauDeBordEstProposé()
    {
        var (shell, _, _) = creer();

        Assert.Equal(new[] { "Tableau de bord" }, shell.Items.Select(i => i.Libelle).ToArray());
        Assert.Equal("(hors session)", shell.NomUtilisateur);
        Assert.Empty(shell.RoleUtilisateur);
        Assert.Equal(TableauDeBord, shell.ItemSelectionne!.Cible);
    }

    [Fact]
    public void Administrateur_LesTroisÉcransSousLesMains()
    {
        var (shell, session, _) = creer();

        session.SeConnecter(MatriceParDefaut.Administrateur);

        Assert.Equal(
            new[] { "Tableau de bord", "Exemples", "Rôles et permissions" },
            shell.Items.Select(i => i.Libelle).ToArray());
        Assert.Equal("Agent Test", shell.NomUtilisateur);
        Assert.Equal(MatriceParDefaut.Administrateur, shell.RoleUtilisateur);
    }

    [Fact]
    public void Utilisateur_LesTroisÉcransAussiCarIlHériteDeLecteur()
    {
        var (shell, session, _) = creer();

        session.SeConnecter(MatriceParDefaut.Utilisateur);

        // Utilisateur tient exemples.consulter et, par héritage de Lecteur, droits.consulter.
        Assert.Equal(3, shell.Items.Count);
    }

    [Fact]
    public void RôleDuProduit_SansDroitExemples_LentréeDisparaît()
    {
        var grille = new MatriceRoles(new[]
        {
            new RoleDefini(MatriceParDefaut.Administrateur, "Administrateur", new[] { Droits.Tout }),
            new RoleDefini("Accueil", "Accueil", new[] { Droits.DroitsConsulter }),
        });

        var (shell, session, _) = creer(grille);
        session.SeConnecter("Accueil");

        Assert.Equal(new[] { "Tableau de bord", "Rôles et permissions" }, shell.Items.Select(i => i.Libelle).ToArray());
        Assert.DoesNotContain(shell.Items, i => i.Cible == typeof(ExempleViewModel));
    }

    [Fact]
    public void Réflexe_DroitSurUnItemEstCeluiDeLaMatricePasDuLibellé()
    {
        var (shell, _, _) = creer(role: MatriceParDefaut.Administrateur);

        Assert.Null(shell.Items.Single(i => i.Libelle == "Tableau de bord").PermissionRequise);
        Assert.Equal(Droits.ExemplesConsulter, shell.Items.Single(i => i.Libelle == "Exemples").PermissionRequise);
        Assert.Equal(Droits.DroitsConsulter, shell.Items.Single(i => i.Libelle == "Rôles et permissions").PermissionRequise);
    }

    [Fact]
    public void SessionQuiTombe_LÉcranProtégéEstRenduDeForce()
    {
        var grille = new MatriceRoles(new[]
        {
            new RoleDefini(MatriceParDefaut.Administrateur, "Administrateur", new[] { Droits.Tout }),
            new RoleDefini("Accueil", "Accueil", new[] { Droits.DroitsConsulter }),
        });

        var (shell, session, navigation) = creer(grille);
        session.SeConnecter(MatriceParDefaut.Administrateur);

        Naviguer(shell, typeof(ExempleViewModel));
        Assert.Equal(typeof(ExempleViewModel), navigation.CurrentViewModelType);

        // Support qui reprend la main sur un poste laissé ouvert : le poste
        // redevient celui du rôle, l'écran interdit est rendu.
        session.SeConnecter("Accueil");

        Assert.DoesNotContain(shell.Items, i => i.Cible == typeof(ExempleViewModel));
        Assert.Equal(TableauDeBord, navigation.CurrentViewModelType);
        Assert.Equal("Tableau de bord", shell.TitreCourant);
    }

    [Fact]
    public void NavigationDepuisLaBarre_LeTitreSuit()
    {
        var (shell, _, navigation) = creer(role: MatriceParDefaut.Administrateur);

        shell.ItemSelectionne = shell.Items.Single(i => i.Cible == typeof(DroitsViewModel));

        Assert.Equal(typeof(DroitsViewModel), navigation.CurrentViewModelType);
        Assert.Equal("Rôles et permissions", shell.TitreCourant);
        Assert.True(navigation.CanGoBack);
    }

    private static void Naviguer(ShellViewModel shell, Type cible)
        => shell.NaviguerCommand.Execute(shell.Items.Single(i => i.Cible == cible));

    private sealed class ThemeEnMemoire : IThemeService
    {
        public AppTheme CurrentTheme { get; private set; } = AppTheme.Light;
        public string AccentColor { get; private set; } = "#0F62FE";

        public event EventHandler<AppTheme>? ThemeChanged;

        public void ApplySavedSettings() { }

        public void SetTheme(AppTheme theme)
        {
            CurrentTheme = theme;
            ThemeChanged?.Invoke(this, theme);
        }

        public void ToggleTheme() => SetTheme(CurrentTheme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);

        public void SetAccentColor(string hexColor) => AccentColor = hexColor;
    }

    /// <summary>Les alertes d'un shell ne se testent pas ici : ce sont les droits qui passent.</summary>
    private sealed class AlertesSilencieuses : IAlertService
    {
        public void Notify(AlertKind kind, string message) { }
    }
}
