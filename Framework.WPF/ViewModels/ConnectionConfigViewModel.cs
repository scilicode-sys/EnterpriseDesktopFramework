using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Framework.Core.Settings;
using Framework.Data.Postgres;
using Microsoft.Extensions.Logging;

namespace Framework.WPF.ViewModels;

/// <summary>
/// Assistant graphique de configuration réseau : teste la connexion PostgreSQL,
/// crée la base du produit si elle manque, puis écrit les réglages dans le
/// settings.json local. Autonome — aucune dépendance au shell ni à la session.
/// Le mot de passe arrive du code-behind (PasswordBox), jamais par une liaison.
/// </summary>
public partial class ConnectionConfigViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ILogger<ConnectionConfigViewModel> _logger;
    private readonly PostgresConnectionResolver _resolver;

    [ObservableProperty]
    private string _hote = "localhost";

    [ObservableProperty]
    private string _port = "5432";

    [ObservableProperty]
    private string _base = string.Empty;

    [ObservableProperty]
    private string _utilisateur = "postgres";

    [ObservableProperty]
    private string _motDePasse = string.Empty;

    [ObservableProperty]
    private bool _sslRequis;

    [ObservableProperty]
    private bool _estOccupe;

    [ObservableProperty]
    private string? _messageResultat;

    [ObservableProperty]
    private bool _resultatPositif;

    /// <summary>True quand l'utilisateur ferme la fenêtre après un enregistrement réussi.</summary>
    public bool EstConfigure { get; private set; }

    public ConnectionConfigViewModel(
        ISettingsService settings,
        ILogger<ConnectionConfigViewModel> logger,
        PostgresConnectionResolver resolver)
    {
        _settings = settings;
        _logger = logger;
        _resolver = resolver;

        // Reprise des réglages existants — l'assistant relit ce qu'il avait écrit.
        var existants = settings.Get(PostgresConnectionResolver.SettingsKey, new PostgresConnectionSettings());

        if (!string.IsNullOrEmpty(existants.Host))
        {
            Hote = existants.Host;
        }

        Port = existants.Port.ToString();
        Base = existants.Database;
        Utilisateur = existants.Username;
        SslRequis = existants.SslRequired;
    }

    /// <summary>Les réglages présents à l'écran, tels que l'assistant les testerait.</summary>
    public PostgresConnectionSettings ReglagesActuels()
    {
        return new PostgresConnectionSettings
        {
            Host = Hote.Trim(),
            Database = Base.Trim(),
            Username = Utilisateur.Trim(),
            Password = MotDePasse,
            SslRequired = SslRequis,
            Port = int.TryParse(Port, out var port) ? port : 5432,
        };
    }

    [RelayCommand]
    private async Task TesterAsync()
    {
        if (!ValiderFormulaire())
        {
            return;
        }

        try
        {
            EstOccupe = true;
            MessageResultat = null;

            var resultat = await PostgresConnectionResolver.TestConnectionAsync(
                ReglagesActuels(), logger: _logger);

            ResultatPositif = resultat.Status == PgConnectionStatus.Ok;
            MessageResultat = resultat.MessageUtilisateur;
        }
        finally
        {
            EstOccupe = false;
        }
    }

    [RelayCommand]
    private async Task EnregistrerAsync()
    {
        if (!ValiderFormulaire())
        {
            return;
        }

        try
        {
            EstOccupe = true;
            MessageResultat = null;

            var reglages = ReglagesActuels();

            var connexion = await PostgresConnectionResolver.TestConnectionAsync(reglages, logger: _logger);
            if (connexion.Status == PgConnectionStatus.BaseIntrouvable)
            {
                var creation = await PostgresConnectionResolver.GarantirBaseAsync(reglages, logger: _logger);

                if (creation.Status != PgConnectionStatus.Ok)
                {
                    ResultatPositif = false;
                    MessageResultat = creation.MessageUtilisateur
                        + "\n\nLa base est absente et le compte indiqué ne peut pas la créer.";
                    return;
                }

                MessageResultat = "La base a été créée sur le serveur.";
            }
            else if (connexion.Status != PgConnectionStatus.Ok)
            {
                ResultatPositif = false;
                MessageResultat = connexion.MessageUtilisateur;
                return;
            }

            _settings.Set(PostgresConnectionResolver.SettingsKey, reglages);

            // Le poste continue sans redémarrer : le resolver singleton relit
            // les réglages, la fabrique EF et l'écouteur repartent dessus.
            _resolver.Actualiser();

            EstConfigure = true;
            ResultatPositif = true;
            MessageResultat = (MessageResultat is null ? "" : MessageResultat + "\n")
                + "Réglages enregistrés : ce poste est prêt.";
        }
        finally
        {
            EstOccupe = false;
        }
    }

    private bool ValiderFormulaire()
    {
        ResultatPositif = false;

        if (string.IsNullOrWhiteSpace(Hote) || string.IsNullOrWhiteSpace(Base) || string.IsNullOrWhiteSpace(Utilisateur))
        {
            MessageResultat = "Serveur, base et utilisateur sont obligatoires.";
            return false;
        }

        if (!int.TryParse(Port, out var port) || port is < 1 or > 65535)
        {
            MessageResultat = "Le port doit être un nombre entre 1 et 65535.";
            return false;
        }

        return true;
    }
}
