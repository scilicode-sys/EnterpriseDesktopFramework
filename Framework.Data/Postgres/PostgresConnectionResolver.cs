using Framework.Core.Settings;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Framework.Data.Postgres;

/// <summary>
/// Source unique de la chaîne de connexion du framework. La fabrique
/// BuildConnectionString ne doit jamais être recopiée ailleurs.
/// </summary>
public class PostgresConnectionResolver
{
    public const string SettingsKey = "Postgres.ConnectionSettings";

    /// <summary>
    /// Identifiant du poste/processus, transmis à PostgreSQL via « Application Name ».
    /// Le trigger de synchronisation l'inclut dans le payload (champ « sender »),
    /// ce qui permet à l'écouteur d'ignorer ses propres écritures (anti-écho).
    /// </summary>
    public static readonly string ClientSyncId =
        $"framework-{Environment.MachineName}-{Environment.ProcessId}";

    private readonly ISettingsService _settingsService;
    private readonly ILogger<PostgresConnectionResolver> _logger;

    private PostgresConnectionSettings _settings = new();

    public string ConnectionString { get; private set; } = string.Empty;

    public string Host { get; private set; } = string.Empty;

    public int Port { get; private set; } = 5432;

    public string Database { get; private set; } = string.Empty;

    public string Username { get; private set; } = string.Empty;

    public string Password { get; private set; } = string.Empty;

    public PostgresConnectionResolver(ISettingsService settingsService, ILogger<PostgresConnectionResolver> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
        Charger();
    }

    /// <summary>
    /// Relit les réglages réseau : à appeler après un enregistrement depuis
    /// l'assistant. Les contextes EF créés ensuite (fabrique) et les
    /// reconnexions de l'écouteur prennent la nouvelle chaîne sans redémarrer.
    /// </summary>
    public void Actualiser()
    {
        Charger();
        _logger.LogInformation("Resolver de connexion ressourcé : {Host}:{Port}/{Database}.", Host, Port, Database);
    }

    private void Charger()
    {
        var settings = _settingsService.Get(SettingsKey, new PostgresConnectionSettings());

        _settings = settings;
        Host = settings.Host;
        Port = settings.Port;
        Database = settings.Database;
        Username = settings.Username;
        Password = settings.Password;
        ConnectionString = BuildConnectionString(settings);
    }

    public Task<PgConnectionResult> TestConnectionAsync(
        int timeoutMs = 5000,
        CancellationToken cancellationToken = default)
        => TestConnectionAsync(_settings, timeoutMs, cancellationToken);

    /// <summary>
    /// Test sur des réglages non encore enregistrés (assistant réseau).
    /// Ne lève jamais d'exception : le résultat porte le statut et le message.
    /// </summary>
    public static async Task<PgConnectionResult> TestConnectionAsync(
        PostgresConnectionSettings settings,
        int timeoutMs = 5000,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        PgConnectionResult Resultat(PgConnectionStatus status, string? detail = null) => new()
        {
            Status = status,
            DetailTechnique = detail,
            Host = settings.Host,
            Port = settings.Port,
            Database = settings.Database,
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            await using var connection = new NpgsqlConnection(BuildConnectionString(settings));
            await connection.OpenAsync(timeoutCts.Token);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT 1;";
            await cmd.ExecuteScalarAsync(timeoutCts.Token);

            return Resultat(PgConnectionStatus.Ok);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // L'appelant a rompu : la cancellation se propage, elle n'est pas un diagnostic.
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning("Timeout ({Ms}ms) lors de la connexion PostgreSQL", timeoutMs);
            return Resultat(PgConnectionStatus.ServeurInjoignable, $"Timeout après {timeoutMs}ms");
        }
        catch (PostgresException ex) when (ex.SqlState is "28P01" or "28000")
        {
            logger?.LogError(ex, "Authentification PostgreSQL refusée");
            return Resultat(PgConnectionStatus.AuthentificationEchouee, ex.Message);
        }
        catch (PostgresException ex) when (ex.SqlState == "3D000")
        {
            logger?.LogError(ex, "Base PostgreSQL introuvable : {Database}", settings.Database);
            return Resultat(PgConnectionStatus.BaseIntrouvable, ex.Message);
        }
        catch (NpgsqlException ex)
        {
            logger?.LogWarning(ex, "Serveur PostgreSQL injoignable");
            return Resultat(PgConnectionStatus.ServeurInjoignable, ex.Message);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Erreur inattendue lors du test de connexion PostgreSQL");
            return Resultat(PgConnectionStatus.ErreurInconnue, ex.Message);
        }
    }

    public static string BuildConnectionString(PostgresConnectionSettings s)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = s.Host,
            Port = s.Port,
            Database = s.Database,
            Username = s.Username,
            Password = s.Password,
            SslMode = s.SslRequired ? SslMode.Require : SslMode.Disable,
            ApplicationName = ClientSyncId,
        };

        return builder.ConnectionString;
    }

    /// <summary>
    /// À partir des réglages courants, se connecte à la base de maintenance
    /// « postgres » et crée la base du produit si elle manque. L'assistant
    /// réseau du premier lancement s'en sert ; le framework ne fait JAMAIS
    /// d'EnsureCreated — les tables restent l'affaire des migrations EF.
    /// Ne lève jamais : le résultat porte le statut et le message.
    /// </summary>
    public static async Task<PgConnectionResult> GarantirBaseAsync(
        PostgresConnectionSettings settings,
        int timeoutMs = 15000,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        PgConnectionResult Resultat(PgConnectionStatus status, string? detail = null) => new()
        {
            Status = status,
            DetailTechnique = detail,
            Host = settings.Host,
            Port = settings.Port,
            Database = settings.Database,
        };

        if (!EstNomDeBaseValide(settings.Database))
        {
            return Resultat(PgConnectionStatus.ErreurInconnue,
                $"Nom de base non accepté : « {settings.Database} » (lettres, chiffres, tiret bas ou tiret seulement).");
        }

        var maintenance = new PostgresConnectionSettings
        {
            Host = settings.Host,
            Port = settings.Port,
            Database = "postgres",
            Username = settings.Username,
            Password = settings.Password,
            SslRequired = settings.SslRequired,
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            await using var connection = new NpgsqlConnection(BuildConnectionString(maintenance));
            await connection.OpenAsync(timeoutCts.Token);

            await using (var verifier = connection.CreateCommand())
            {
                verifier.CommandText = "SELECT 1 FROM pg_database WHERE datname = @nom;";
                verifier.Parameters.AddWithValue("nom", settings.Database);
                var existe = await verifier.ExecuteScalarAsync(timeoutCts.Token) is not null;

                if (existe)
                {
                    return Resultat(PgConnectionStatus.Ok);
                }
            }

            // CREATE DATABASE n'accepte aucun paramètre : le nom est validé
            // par whitelist puis passé entre guillemets échappés.
            await using var creer = connection.CreateCommand();
            creer.CommandText = $"CREATE DATABASE \"{settings.Database.Replace("\"", "\"\"")}\";";
            await creer.ExecuteNonQueryAsync(timeoutCts.Token);

            logger?.LogInformation("Base « {Database} » créée sur {Host}:{Port}.", settings.Database, settings.Host, settings.Port);
            return Resultat(PgConnectionStatus.Ok);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning("Timeout ({Ms}ms) lors de la création de la base", timeoutMs);
            return Resultat(PgConnectionStatus.ServeurInjoignable, $"Timeout après {timeoutMs}ms");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Création de la base « {Database} » impossible", settings.Database);
            return Resultat(PgConnectionStatus.ErreurInconnue, ex.Message);
        }
    }

    internal static bool EstNomDeBaseValide(string? nom)
        => !string.IsNullOrEmpty(nom)
        && System.Text.RegularExpressions.Regex.IsMatch(nom, @"^[a-zA-Z_][a-zA-Z0-9_\-]{0,62}$");
}
