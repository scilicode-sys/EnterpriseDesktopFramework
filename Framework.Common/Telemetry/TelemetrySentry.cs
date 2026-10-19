using System.IO;
using Microsoft.Extensions.Logging;
using Sentry;

namespace Framework.Common.Telemetry;

/// <summary>
/// Amorçage Sentry minimal pour le framework : pas de tracing, pas de PII,
/// seuls les événements Error+ sortent. L'hôte fournit le DSN (réglage ou
/// variable d'environnement) ; sans DSN, rien ne démarre et le poste reste
/// muet mais fonctionnel.
/// </summary>
public static class TelemetrySentry
{
    private static bool _actif;

    public static bool Actif => _actif;

    /// <summary>
    /// Initialise le SDK. Priorité des DSN : paramètre explicite, puis
    /// NEXAMED-style variable d'environnement FRAMEWORK_SENTRY_DSN. null/vide = pas de
    /// télémétrie (retour false, aucune exception).
    /// </summary>
    public static bool Demarrer(
        ILogger? logger,
        string? dsnReglages = null,
        string? dsnEnvironnement = null,
        Action<SentryOptions>? configurer = null)
    {
        var dsn = dsnReglages
            ?? Environment.GetEnvironmentVariable("FRAMEWORK_SENTRY_DSN");

        if (string.IsNullOrWhiteSpace(dsn))
        {
            logger?.LogInformation("Télémétrie Sentry hors jeu : aucun DSN fourni.");
            return false;
        }

        try
        {
            SentrySdk.Init(options =>
            {
                options.Dsn = dsn;
                options.TracesSampleRate = 0;
                options.AutoSessionTracking = false;
                options.CacheDirectoryPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Framework", "SentryCache");

                // Seuil Error+ : un [WRN] reste au journal local, Sentry ne voit que les pannes.
                options.SetBeforeSend((evento, _) =>
                    evento.Level >= SentryLevel.Error ? evento : null);

                configurer?.Invoke(options);
            });

            _actif = true;
            logger?.LogInformation("Télémétrie Sentry démarrée (Error+ uniquement).");
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Échec de l'amorçage Sentry : télémétrie désactivée pour cette session.");
            return false;
        }
    }

    public static void Capturer(Exception exception)
    {
        if (_actif)
        {
            SentrySdk.CaptureException(exception);
        }
    }

    public static void CapturerMessage(string message)
    {
        if (_actif)
        {
            SentrySdk.CaptureMessage(message, SentryLevel.Error);
        }
    }

    /// <summary>Vidange le cache avant une fermeture propre (2 s maximum).</summary>
    public static async Task VidangerAsync()
    {
        if (!_actif)
        {
            return;
        }

        try
        {
            await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // Une fermeture ne se juge jamais sur un échec de télémétrie.
        }
    }

    public static void Arreter()
    {
        if (!_actif)
        {
            return;
        }

        SentrySdk.Close();
        _actif = false;
    }
}
