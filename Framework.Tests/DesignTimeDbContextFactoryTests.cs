using System.IO;
using Framework.Data.Design;
using Framework.Data.Postgres;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Framework.Tests;

/// <summary>
/// Garde-fous de la fabrique de contexte de conception : c'est le chemin que
/// suivra chaque poste neuf pour créer la base (<c>dotnet ef database update</c>).
/// Deux ratés à couvrir : une configuration absente doit refuser explicitement
/// de générer une base quelconque, et une configuration présente doit produire
/// la chaîne d'anti-écho (« Application Name ») sinon les triggers notifieront
/// le poste lui-même en boucle.
/// </summary>
public sealed class DesignTimeDbContextFactoryTests : IDisposable
{
    private const string NomTest = "FrameworkTestsDesignTime";

    private readonly string _dossier;
    private readonly string? _valeurInitiale;

    public DesignTimeDbContextFactoryTests()
    {
        _dossier = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), NomTest);

        Directory.CreateDirectory(_dossier);
        _valeurInitiale = Environment.GetEnvironmentVariable(DesignTimeDbContextFactory.VariableNomApplication);
        Environment.SetEnvironmentVariable(DesignTimeDbContextFactory.VariableNomApplication, NomTest);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(
            DesignTimeDbContextFactory.VariableNomApplication, _valeurInitiale);

        try
        {
            Directory.Delete(_dossier, true);
        }
        catch (IOException)
        {
            // Dossier déjà libéré : rien à nettoyer.
        }
    }

    [Fact]
    public void CreateDbContext_ReglagesAbsents_LeveInvalidOperationException()
    {
        EcrireReglages("""{ "Autre.Clé": 1 }""");

        var fabrique = new DesignTimeDbContextFactory();

        var ex = Assert.Throws<InvalidOperationException>(() => fabrique.CreateDbContext(Array.Empty<string>()));
        Assert.Contains("assistant réseau", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateDbContext_HoteVide_LeveInvalidOperationException()
    {
        EcrireReglages("""
            { "Postgres.ConnectionSettings": { "host": "  ", "port": 5432,
              "database": "x", "username": "u", "password": "p" } }
            """);

        var fabrique = new DesignTimeDbContextFactory();

        Assert.Throws<InvalidOperationException>(() => fabrique.CreateDbContext(Array.Empty<string>()));
    }

    [Fact]
    public void CreateDbContext_ReglagesPresents_ChaineCompleteAvecAntiEcho()
    {
        EcrireReglages("""
            { "Postgres.ConnectionSettings": { "host": "db.local", "port": 5433,
              "database": "framework_demo", "username": "demo", "password": "secret-de-test",
              "sslRequired": false } }
            """);

        var fabrique = new DesignTimeDbContextFactory();
        using var contexte = fabrique.CreateDbContext(Array.Empty<string>());

        var chaine = contexte.Database.GetConnectionString();
        Assert.Contains("Host=db.local", chaine, StringComparison.Ordinal);
        Assert.Contains("Port=5433", chaine, StringComparison.Ordinal);
        Assert.Contains("Database=framework_demo", chaine, StringComparison.Ordinal);
        Assert.Contains($"Application Name={PostgresConnectionResolver.ClientSyncId}",
            chaine, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateDbContext_SslRequis_ModeDeChiffreActive()
    {
        EcrireReglages("""
            { "Postgres.ConnectionSettings": { "host": "db.local", "port": 5432,
              "database": "cad", "username": "demo", "password": "secret-de-test",
              "sslRequired": true } }
            """);

        var fabrique = new DesignTimeDbContextFactory();
        using var contexte = fabrique.CreateDbContext(Array.Empty<string>());

        Assert.Contains("SSL Mode=Require", contexte.Database.GetConnectionString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CreateDbContext_MigreLeMemeModeleQueLeContexteDeProduction()
    {
        EcrireReglages("""
            { "Postgres.ConnectionSettings": { "host": "db.local", "port": 5432,
              "database": "framework_demo", "username": "demo", "password": "secret-de-test" } }
            """);

        var fabrique = new DesignTimeDbContextFactory();
        using var contexte = fabrique.CreateDbContext(Array.Empty<string>());

        // La fabrique doit exposer les mêmes tables que l'application : sinon la
        // migration « Initial » générée ne correspondrait pas au schéma réel.
        var tables = contexte.Model.GetEntityTypes()
            .Select(t => t.GetTableName())
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "Comptes", "Exemples" }, tables);
    }

    private void EcrireReglages(string json)
        => File.WriteAllText(Path.Combine(_dossier, "settings.json"), json);
}
