using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace Framework.Common.Update;

/// <summary>
/// Wrapper Velopack : la fabrique rend null hors installation Velopack
/// (démarrage de développement) et ne lève jamais. Une mise à jour
/// téléchargée est mise en file d'attente, présentée à l'écran, et appliquée
/// uniquement quand l'hôte appelle AppliquerALaFermeture depuis OnExit —
/// jamais l'updater ne part pendant que l'utilisateur travaille.
/// </summary>
public sealed class VelopackUpdateService
{
    public const string CanalParDefaut = "beta";

    private readonly UpdateManager _manager;
    private readonly ILogger<VelopackUpdateService> _logger;

    private VelopackUpdateService(UpdateManager manager, ILogger<VelopackUpdateService> logger)
    {
        _manager = manager;
        _logger = logger;
    }

    /// <summary>Première instruction du démarrage : donne la main à Velopack avant toute fenêtre.</summary>
    public static void DemarrerTantQuePointDentree() => VelopackApp.Build().Run();

    /// <summary>
    /// Rend l'instance si le poste tourne sur une installation Velopack, null sinon.
    /// Un feed irrésoluble rend un [WRN] et null : un poste de développement ne voit
    /// jamais un [ERR] de mise à jour, un poste cassé le voit écrit.
    /// </summary>
    public static VelopackUpdateService? CreerSiInstalle(
        ILogger<VelopackUpdateService> logger,
        string depotUrl,
        string canal = CanalParDefaut,
        string? urlOuCheminFeed = null)
    {
        UpdateManager manager;

        try
        {
            var options = new UpdateOptions { ExplicitChannel = canal };

            // URL absolue du dépôt GitHub Releases du produit ; un slug sans schéma
            // rendrait une URI relative silencieusement morte (mesure du dépôt hôte).
            manager = urlOuCheminFeed is null
                ? new UpdateManager(new GithubSource(depotUrl, null, false), options)
                : new UpdateManager(urlOuCheminFeed, options);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Velopack n'a pas pu ouvrir le feed de ce poste : mise à jour ignorée.");
            return null;
        }

        try
        {
            if (!manager.IsInstalled)
            {
                logger.LogInformation("Démarrage hors installation Velopack : vérification de mise à jour ignorée.");
                return null;
            }

            return new VelopackUpdateService(manager, logger);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Environnement Velopack non résoluble sur ce poste : mise à jour ignorée.");
            return null;
        }
    }

    public string VersionCourante => _manager.CurrentVersion?.ToString() ?? "inconnue";

    /// <summary>Consulte le feed ; rend l'information de mise à jour, null si le poste est à jour.</summary>
    public async Task<UpdateInfo?> VerifierAsync(CancellationToken cancellationToken = default)
    {
        var update = await _manager.CheckForUpdatesAsync();

        if (update is null)
        {
            _logger.LogInformation("Vérification faite : aucune mise à jour disponible (version {Version}).", VersionCourante);
        }

        return update;
    }

    /// <summary>Télécharge le paquet ; Velopack vérifie ses propres hashes à la fin.</summary>
    public async Task TelechargerAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Téléchargement de la mise à jour vers {Version}…", update.TargetFullRelease.Version);
        await _manager.DownloadUpdatesAsync(update, _ => { }, cancellationToken);
        _logger.LogInformation("Téléchargement terminé et hashes vérifiés : {Version}.", update.TargetFullRelease.Version);
    }

    /// <summary>Version déjà téléchargée, en attente de redémarrage. null si rien n'attend.</summary>
    public string? VersionEnFileAttente()
    {
        var enAttente = _manager.UpdatePendingRestart;
        return enAttente is null ? null : enAttente.Version.ToString();
    }

    /// <summary>
    /// Le SEUL point où l'updater a le droit de démarrer : l'appelant est déjà en
    /// train de sortir (OnExit). WaitExitThenApplyUpdates ne lui laisse que 60 s
    /// pour attendre la sortie du programme — appelé plus tôt, il tue l'application.
    /// </summary>
    public bool AppliquerALaFermeture(bool redemarrer)
    {
        var enAttente = _manager.UpdatePendingRestart;
        if (enAttente is null)
        {
            return false;
        }

        // Positionnel, comme dans le poste hôte dont la voie est mesurée :
        // (update, silent, restartApp).
        _manager.WaitExitThenApplyUpdates(enAttente, !redemarrer, redemarrer);
        return true;
    }
}
