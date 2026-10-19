namespace Framework.Core.Sync;

/// <summary>
/// Écouteur du temps réel inter-postes. Les événements sont émis sur un fil
/// d'arrière-plan : la couche présentation doit maréchaliser elle-même sur son Dispatcher.
/// </summary>
public interface ISyncNotificationService : IAsyncDisposable
{
    event EventHandler<SyncNotificationDto>? EntityChanged;

    bool IsConnected { get; }

    /// <summary>Identifiant de ce poste, comparé au champ « sender » pour l'anti-écho.</summary>
    string ClientSyncId { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    bool IsFromSelf(SyncNotificationDto notification);
}
