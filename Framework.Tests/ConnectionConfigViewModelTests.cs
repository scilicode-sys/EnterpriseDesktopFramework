using Framework.Core.Settings;
using Framework.Data.Postgres;
using Framework.WPF.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Framework.Tests;

/// <summary>
/// Logique de l'assistant réseau, testée sans fenêtre : validation du
/// formulaire, diagnostic de test, refus d'enregistrer sur un poste injoignable,
/// reprise des réglages existants.
/// </summary>
public class ConnectionConfigViewModelTests
{
    private static (ConnectionConfigViewModel vue, ReglagesEnMemoire settings) CreerAssistant(
        PostgresConnectionSettings? existants = null)
    {
        ISettingsService settings = new ReglagesEnMemoire();

        if (existants is not null)
        {
            settings.Set(PostgresConnectionResolver.SettingsKey, existants);
        }

        var resolver = new PostgresConnectionResolver(settings, NullLogger<PostgresConnectionResolver>.Instance);

        return (new ConnectionConfigViewModel(
            settings,
            NullLogger<ConnectionConfigViewModel>.Instance,
            resolver), (ReglagesEnMemoire)settings);
    }

    [Fact]
    public void NouveauPoste_ChampVideParConception()
    {
        var (vue, _) = CreerAssistant();

        Assert.Equal("localhost", vue.Hote);
        Assert.Equal("5432", vue.Port);
        Assert.Equal("", vue.Base);
        Assert.Equal("", vue.MotDePasse);
        Assert.False(vue.EstConfigure);
    }

    [Fact]
    public void RéglagesExistants_SontRepérés()
    {
        var (vue, _) = CreerAssistant(new PostgresConnectionSettings
        {
            Host = "10.1.2.3",
            Port = 5433,
            Database = "auto_ecole",
            Username = "amara",
            SslRequired = true,
        });

        Assert.Equal("10.1.2.3", vue.Hote);
        Assert.Equal("5433", vue.Port);
        Assert.Equal("auto_ecole", vue.Base);
        Assert.Equal("amara", vue.Utilisateur);
        Assert.True(vue.SslRequis);
    }

    [Fact]
    public void ReglagesActuels_TrimEtPortNumérique()
    {
        var (vue, _) = CreerAssistant();
        vue.Hote = "  db.local  ";
        vue.Base = " salon ";
        vue.Utilisateur = " postgres ";
        vue.Port = "pas-un-nombre";

        var reglages = vue.ReglagesActuels();

        Assert.Equal("db.local", reglages.Host);
        Assert.Equal("salon", reglages.Database);
        Assert.Equal("postgres", reglages.Username);
        Assert.Equal(5432, reglages.Port);
    }

    [Theory]
    [InlineData("", "base", "user")]
    [InlineData("hote", "", "user")]
    [InlineData("hote", "base", "")]
    public async Task Tester_ChampsManquants_MessageSansAppelRéseau(string hote, string basen, string user)
    {
        var (vue, _) = CreerAssistant();
        vue.Hote = hote;
        vue.Base = basen;
        vue.Utilisateur = user;

        await vue.TesterCommand.ExecuteAsync(null);

        Assert.Equal("Serveur, base et utilisateur sont obligatoires.", vue.MessageResultat);
        Assert.False(vue.ResultatPositif);
    }

    [Fact]
    public async Task Tester_PortFoubille_DemandeCorrectionAvantRéseau()
    {
        var (vue, _) = CreerAssistant();
        vue.Hote = "127.0.0.1";
        vue.Base = "base";
        vue.Utilisateur = "user";
        vue.Port = "99999";

        await vue.TesterCommand.ExecuteAsync(null);

        Assert.Equal("Le port doit être un nombre entre 1 et 65535.", vue.MessageResultat);
    }

    [Fact]
    public async Task Tester_ServeurInjoignable_DiagnosticFrançaisPasDeSauvegarde()
    {
        var (vue, settings) = CreerAssistant();
        vue.Hote = "127.0.0.1";
        vue.Port = "1";
        vue.Base = "base_test";
        vue.Utilisateur = "user";

        await vue.TesterCommand.ExecuteAsync(null);

        Assert.False(vue.ResultatPositif);
        Assert.Contains("PostgreSQL est démarré", vue.MessageResultat);
        Assert.False(settings.Has(PostgresConnectionResolver.SettingsKey));
    }

    [Fact]
    public async Task Enregistrer_ServeurInjoignable_NEnregistreRien()
    {
        var (vue, settings) = CreerAssistant();
        vue.Hote = "127.0.0.1";
        vue.Port = "1";
        vue.Base = "base_test";
        vue.Utilisateur = "user";

        await vue.EnregistrerCommand.ExecuteAsync(null);

        Assert.False(vue.EstConfigure);
        Assert.False(settings.Has(PostgresConnectionResolver.SettingsKey));
    }
}
