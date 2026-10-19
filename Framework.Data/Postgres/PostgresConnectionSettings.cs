namespace Framework.Data.Postgres;

/// <summary>
/// Paramètres de connexion PostgreSQL, persistés via ISettingsService.
/// Les champs d'identité sont vides **par conception** : sans réglages,
/// l'assistant réseau du poste les demande — aucun identifiant en dur ici.
/// </summary>
public class PostgresConnectionSettings
{
    public string Host { get; set; } = "";

    public int Port { get; set; } = 5432;

    public string Database { get; set; } = "";

    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    public bool SslRequired { get; set; }
}

public enum PgConnectionStatus
{
    Ok,
    ServeurInjoignable,
    AuthentificationEchouee,
    BaseIntrouvable,
    ErreurInconnue,
}

/// <summary>Résultat du test de connexion : statut + message français prêt pour l'écran.</summary>
public class PgConnectionResult
{
    public PgConnectionStatus Status { get; init; }

    public string? DetailTechnique { get; init; }

    public string? Host { get; init; }

    public int? Port { get; init; }

    public string? Database { get; init; }

    private string Cible => string.IsNullOrEmpty(Host)
        ? "serveur de base de données"
        : $"{Host}{(Port is > 0 ? ":" + Port : "")}/{Database}";

    public string MessageUtilisateur => Status switch
    {
        PgConnectionStatus.Ok => "Connexion établie.",
        PgConnectionStatus.ServeurInjoignable =>
            $"Impossible de joindre le serveur « {Cible} ».\n\n" +
            "1. Vérifiez que PostgreSQL est démarré sur l'ordinateur indiqué.\n" +
            "2. Vérifiez les autorisations de connexion réseau (listen_addresses, pg_hba.conf).\n" +
            "3. Vérifiez le pare-feu de ce poste.",
        PgConnectionStatus.AuthentificationEchouee =>
            "Connexion refusée par le serveur : utilisateur ou mot de passe incorrect.",
        PgConnectionStatus.BaseIntrouvable =>
            $"La base « {Database} » n'existe pas sur ce serveur.",
        _ =>
            $"Erreur inattendue lors de la connexion à « {Cible} ».\n" +
            "Contactez le support technique.",
    };
}
