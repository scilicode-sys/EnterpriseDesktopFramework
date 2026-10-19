using System.IO;
using System.Text.Json;
using Framework.Core.Settings;
using Framework.Data.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;

namespace Framework.Data.Design;

/// <summary>
/// Fabrique de contexte pour les outils EF Core (<c>dotnet ef migrations add</c>,
/// <c>dotnet ef database update</c>). Elle vit dans l'assembly des migrations,
/// lit le même <c>settings.json</c> que l'application (écrit par l'assistant
/// réseau) et reconstruit la chaîne via
/// <see cref="PostgresConnectionResolver.BuildConnectionString"/> — jamais
/// recopiée — pour que l'« Application Name » d'anti-écho accompagne aussi
/// les sessions de migration.
/// </summary>
/// <remarks>
/// Recopier la mécanique de <c>JsonSettingsService</c> (lecture d'un dictionnaire
/// JSON, désérialisation tolante) est délibéré : <c>Framework.Common</c> dépend
/// de <c>Framework.Data</c>, un aller-retour fermerait un cycle entre les deux
/// socles. Le fichier de réglages reste la seule source de vérité.
/// Nom de l'installation : variable d'environnement <c>FRAMEWORK_APP_NAME</c>,
/// sinon « FrameworkDemo » (démo) — un produit dérivé passe la variable plutôt
/// que de retoucher ce fichier.
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FrameworkDbContext>
{
    public const string VariableNomApplication = "FRAMEWORK_APP_NAME";
    public const string NomApplicationParDefaut = "FrameworkDemo";

    private static readonly JsonSerializerOptions LectureOptions = new(JsonSerializerDefaults.Web);

    public FrameworkDbContext CreateDbContext(string[] args)
    {
        var reglages = LireReglages();

        if (string.IsNullOrWhiteSpace(reglages.Host)
            || string.IsNullOrWhiteSpace(reglages.Database)
            || string.IsNullOrWhiteSpace(reglages.Username))
        {
            throw new InvalidOperationException(
                "PostgreSQL non configuré sur ce poste : lancez l'application une fois et renseignez "
                + "l'assistant réseau (« Configuration réseau de ce poste ») avant d'utiliser dotnet ef, "
                + "ou renseignez directement la clé « Postgres.ConnectionSettings » dans "
                + "%LocalAppData%\\" + NomApplication() + "\\settings.json.");
        }

        var resolver = new PostgresConnectionResolver(
            new ReglagesFichier(NomApplication()), NullLogger<PostgresConnectionResolver>.Instance);

        var builder = new DbContextOptionsBuilder<FrameworkDbContext>();
        builder.UseNpgsql(resolver.ConnectionString, npgsql => npgsql
            .EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null)
            .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));

        return new FrameworkDbContext(builder.Options);
    }

    private static string NomApplication()
        => Environment.GetEnvironmentVariable(VariableNomApplication) is { Length: > 0 } nom
            ? nom
            : NomApplicationParDefaut;

    private static PostgresConnectionSettings LireReglages()
    {
        var chemin = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            NomApplication(), "settings.json");

        if (!File.Exists(chemin))
        {
            return new PostgresConnectionSettings();
        }

        using var document = JsonDocument.Parse(File.ReadAllText(chemin));

        if (!document.RootElement.TryGetProperty(
                PostgresConnectionResolver.SettingsKey, out var noeud))
        {
            return new PostgresConnectionSettings();
        }

        return noeud.Deserialize<PostgresConnectionSettings>(LectureOptions) ?? new PostgresConnectionSettings();
    }

    /// <summary>Lecture seule, bornée à la clé de connexion : aucun écrit, aucun secret affiché.</summary>
    private sealed class ReglagesFichier : ISettingsService
    {
        private readonly Dictionary<string, JsonElement> _racine;

        public ReglagesFichier(string appName)
        {
            var chemin = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                appName, "settings.json");

            _racine = File.Exists(chemin)
                ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    File.ReadAllText(chemin), LectureOptions) ?? new()
                : new();
        }

        public T Get<T>(string key, T defaultValue)
            => _racine.TryGetValue(key, out var noeud)
                ? noeud.Deserialize<T>(LectureOptions) ?? defaultValue
                : defaultValue;

        public void Set<T>(string key, T value)
            => throw new EcritureRefuseeException();

        public bool TryGet<T>(string key, out T? value)
        {
            value = default;

            if (!_racine.TryGetValue(key, out var noeud))
            {
                return false;
            }

            try
            {
                value = noeud.Deserialize<T>(LectureOptions);
                return value is not null;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                return false;
            }
        }

        public bool Has(string key) => _racine.ContainsKey(key);

        public void Delete(string key) => throw new EcritureRefuseeException();

        /// <summary>Cette fabrique ne fait qu'écrire la base : elle ne touche jamais les réglages.</summary>
        private sealed class EcritureRefuseeException : NotSupportedException
        {
            public EcritureRefuseeException()
                : base("Les réglages sont en lecture seule à la conception : c'est l'assistant réseau qui les écrit.")
            {
            }
        }
    }
}
