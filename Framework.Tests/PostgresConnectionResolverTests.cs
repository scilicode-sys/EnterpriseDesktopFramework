using Framework.Data.Postgres;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Framework.Tests;

/// <summary>
/// Logique réseau du socle : fabrique de chaîne de connexion (anti-écho
/// ApplicationName, SSL), diagnostic par SQLSTATE, rechargement à chaud
/// après l'assistant, whitelist du nom de base en création.
/// </summary>
public class PostgresConnectionResolverTests
{
    private static PostgresConnectionSettings Reglages(
        string hote = "db.local",
        int port = 5433,
        string base_ = "mon_app",
        bool ssl = false) => new()
        {
            Host = hote,
            Port = port,
            Database = base_,
            Username = "postgres",
            Password = "secret-test",
            SslRequired = ssl,
        };

    [Fact]
    public void BuildConnectionString_ContientTouteLaSignature()
    {
        var chaine = PostgresConnectionResolver.BuildConnectionString(Reglages());

        Assert.Contains("Host=db.local", chaine);
        Assert.Contains("Port=5433", chaine);
        Assert.Contains("Database=mon_app", chaine);
        Assert.Contains("Username=postgres", chaine);

        // Le vecteur d'anti-écho : l'ApplicationName doit être l'identité du poste.
        // Npgsql rend la clé « Application Name » avec son espace dans la chaîne.
        Assert.Contains($"Application Name={PostgresConnectionResolver.ClientSyncId}", chaine);
        Assert.Contains("SSL Mode=Disable", chaine);
    }

    [Fact]
    public void BuildConnectionString_SslRequis_ModeRequire()
    {
        Assert.Contains("SSL Mode=Require", PostgresConnectionResolver.BuildConnectionString(Reglages(ssl: true)));
    }

    [Fact]
    public void ClientSyncId_MachineEtProcessus()
    {
        Assert.StartsWith($"framework-{Environment.MachineName}-", PostgresConnectionResolver.ClientSyncId);
    }

    [Fact]
    public void Resolver_SansRéglages_PartEnVide()
    {
        var resolver = new PostgresConnectionResolver(new ReglagesEnMemoire(), NullLogger<PostgresConnectionResolver>.Instance);

        Assert.Equal("", resolver.Host);
        Assert.Equal(5432, resolver.Port);
    }

    [Fact]
    public void Actualiser_RelitLesRéglagesSansRedémarrage()
    {
        var settings = new ReglagesEnMemoire();
        var resolver = new PostgresConnectionResolver(settings, NullLogger<PostgresConnectionResolver>.Instance);

        Assert.Equal("", resolver.Host);

        settings.Set(PostgresConnectionResolver.SettingsKey, Reglages(hote: "10.0.0.5", port: 5432, base_: "auto_ecole"));
        resolver.Actualiser();

        Assert.Equal("10.0.0.5", resolver.Host);
        Assert.Contains("Database=auto_ecole", resolver.ConnectionString);
    }

    [Fact]
    public async Task TestConnection_ServeurRefusantLeConnexion_SertInjoignable()
    {
        var resultat = await PostgresConnectionResolver.TestConnectionAsync(
            Reglages(hote: "127.0.0.1", port: 1), timeoutMs: 3000);

        Assert.Equal(PgConnectionStatus.ServeurInjoignable, resultat.Status);
        Assert.Contains("PostgreSQL est démarré", resultat.MessageUtilisateur);
    }

    [Fact]
    public async Task TestConnection_AnnulationExterne_RespecteLeJeton()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PostgresConnectionResolver.TestConnectionAsync(Reglages(hote: "127.0.0.1", port: 1), 3000, cts.Token));
    }

    [Fact]
    public async Task GarantirBase_NomDeBaseInvalide_RefusSansAppelRéseau()
    {
        var resultat = await PostgresConnectionResolver.GarantirBaseAsync(
            Reglages(base_: "ma base; DROP TABLE"), timeoutMs: 1000);

        Assert.Equal(PgConnectionStatus.ErreurInconnue, resultat.Status);
        Assert.NotNull(resultat.DetailTechnique);
    }

    [Theory]
    [InlineData("auto_ecole")]
    [InlineData("nexus-med-v2")]
    [InlineData("_privee")]
    [InlineData("")]
    [InlineData("base avec espaces")]
    [InlineData("très-longue-base-qui-dépasse-la-limitautorisee-de-soixante-trois-caracteres-abcdefgh")]
    public async Task GarantirBase_WhitelistNom(string nom)
    {
        var resultat = await PostgresConnectionResolver.GarantirBaseAsync(
            Reglages(hote: "127.0.0.1", port: 1, base_: nom), timeoutMs: 1500);

        var attenduEstInvalide = string.IsNullOrEmpty(nom)
            || nom.Contains(' ')
            || nom.Length > 63;

        // Les noms refusés le sont AVANT tout appel réseau (ErreurInconnue immédiate) ;
        // les noms acceptés partent vers le serveur, ici injoignable.
        if (attenduEstInvalide)
        {
            Assert.Equal(PgConnectionStatus.ErreurInconnue, resultat.Status);
        }
        else
        {
            Assert.NotEqual(PgConnectionStatus.ErreurInconnue, resultat.Status);
        }
    }

    [Fact]
    public void PgConnectionResult_MessageSurBaseIntrouvable_CiteLaBase()
    {
        var resultat = new PgConnectionResult
        {
            Status = PgConnectionStatus.BaseIntrouvable,
            Host = "db.local",
            Port = 5432,
            Database = "mon_app",
        };

        Assert.Contains("« mon_app »", resultat.MessageUtilisateur);
    }
}
