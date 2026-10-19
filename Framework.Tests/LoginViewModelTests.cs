using Framework.Core.Auth;
using Framework.Core.Data;
using Framework.Core.Notifications;
using Framework.WPF.ViewModels;
using Xunit;

namespace Framework.Tests;

/// <summary>
/// Écran de connexion, testé sans fenêtre : le mode « premier compte » doit suivre
/// l'état réel de la table, et une base joignable sans schéma ne doit plus être
/// accusée d'être mal configurée réseau — c'est le diagnostic qui a fait perdre
/// une soirée de mise en service à un poste pourtant câblé.
/// </summary>
public class LoginViewModelTests
{
    private sealed class AuthFaute : IAuthService
    {
        public bool? ComptesPresent { get; init; }

        public Exception? Erreur { get; init; }

        public List<string> Messages { get; } = new();

        public AppUser? CurrentUser => null;

        public bool IsAuthenticated => false;

        public event EventHandler<AppUser?>? AuthenticationStateChanged
        {
            add { }
            remove { }
        }

        public Task<bool> HasAnyAccountAsync(CancellationToken cancellationToken = default)
            => Erreur is not null
                ? Task.FromException<bool>(Erreur)
                : Task.FromResult(ComptesPresent ?? throw new InvalidOperationException("Diagnostic non programmé."));

        public Task<AuthenticationResult> LoginAsync(
            string username, string password, CancellationToken cancellationToken = default)
            => Task.FromResult(new AuthenticationResult(AuthenticationStatus.WrongCredentials, "mot de passe"));

        public Task LogoutAsync() => Task.CompletedTask;

        public Task<AuthenticationResult> CreerPremierCompteAsync(
            string username, string nomComplet, string role, string password,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Non demandé par cet écran.");
    }

    private sealed class AlertesCollectees : IAlertService
    {
        public List<string> Messages { get; } = new();

        public void Notify(AlertKind kind, string message) => Messages.Add($"{kind}:{message}");
    }

    private static LoginViewModel Creer(AuthFaute auth, AlertesCollectees alertes) => new(auth, alertes);

    [Fact]
    public async Task Preparer_TableVide_PasseEnModePremierCompte()
    {
        var alertes = new AlertesCollectees();
        var vue = Creer(new AuthFaute { ComptesPresent = false }, alertes);

        await vue.PreparerAsync();

        Assert.True(vue.ModePremierCompte);
        Assert.Null(vue.MessageErreur);
        Assert.Empty(alertes.Messages);
    }

    [Fact]
    public async Task Preparer_TablePeuplée_ResteEnModeConnexion()
    {
        var vue = Creer(new AuthFaute { ComptesPresent = true }, new AlertesCollectees());

        await vue.PreparerAsync();

        Assert.False(vue.ModePremierCompte);
        Assert.Null(vue.MessageErreur);
    }

    [Fact]
    public async Task Preparer_SchémaAbsent_NAccusePasLaConfigurationRéseau()
    {
        var alertes = new AlertesCollectees();
        var vue = Creer(
            new AuthFaute { Erreur = new SchemaNonInstalleException("Framework_Test") },
            alertes);

        await vue.PreparerAsync();

        Assert.False(vue.ModePremierCompte);
        Assert.Contains("tables sont absentes", vue.MessageErreur);
        Assert.Contains("Framework_Test", vue.MessageErreur);
        Assert.DoesNotContain("configuration réseau", vue.MessageErreur);
        Assert.Single(alertes.Messages);
        Assert.StartsWith("Error:", alertes.Messages[0]);
    }

    [Fact]
    public async Task Preparer_ServeurInjoignable_RenvoieVersLaConfigurationRéseau()
    {
        var alertes = new AlertesCollectees();
        var vue = Creer(new AuthFaute { Erreur = new InvalidOperationException("timeout") }, alertes);

        await vue.PreparerAsync();

        Assert.False(vue.ModePremierCompte);
        Assert.Contains("configuration réseau", vue.MessageErreur);
        Assert.DoesNotContain("tables sont absentes", vue.MessageErreur);
        Assert.Contains("timeout", alertes.Messages[0]);
    }
}
