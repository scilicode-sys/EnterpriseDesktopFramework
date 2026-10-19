using System.Text.Json;
using Framework.Core.Sync;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Framework.Data.Postgres;

/// <summary>
/// Écoute des notifications inter-postes via PostgreSQL LISTEN/NOTIFY.
/// Connexion dédiée hors pool EF, battement de cœur sur WaitAsync pour
/// détecter une connexion morte, backoff exponentiel 1 s → 30 s, anti-écho
/// sur le champ « sender ». Les événements partent d'un fil d'arrière-plan :
/// la couche présentation maréchalise sur son Dispatcher.
/// </summary>
public sealed class PostgresSyncListener : ISyncNotificationService
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BackoffInitial = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BackoffMax = TimeSpan.FromSeconds(30);

    private readonly PostgresConnectionResolver _resolver;
    private readonly ILogger<PostgresSyncListener> _logger;
    private readonly string _channel;
    private readonly CancellationTokenSource _cts = new();

    private Task? _listenLoop;
    private volatile bool _isConnected;

    public event EventHandler<SyncNotificationDto>? EntityChanged;

    public bool IsConnected => _isConnected;

    public string ClientSyncId => PostgresConnectionResolver.ClientSyncId;

    public PostgresSyncListener(
        PostgresConnectionResolver resolver,
        ILogger<PostgresSyncListener> logger,
        string channel = "framework_sync")
    {
        _resolver = resolver;
        _logger = logger;
        _channel = channel;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_listenLoop is not null)
        {
            return Task.CompletedTask;
        }

        _listenLoop = Task.Run(() => ListenLoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _cts.Cancel();

        if (_listenLoop is not null)
        {
            try
            {
                await _listenLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _isConnected = false;
    }

    public bool IsFromSelf(SyncNotificationDto notification)
        => string.Equals(notification.Sender, PostgresConnectionResolver.ClientSyncId, StringComparison.Ordinal);

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        var backoff = BackoffInitial;

        while (!ct.IsCancellationRequested)
        {
            NpgsqlConnection? connection = null;

            try
            {
                connection = new NpgsqlConnection(_resolver.ConnectionString);
                connection.Notification += OnNotification;
                await connection.OpenAsync(ct).ConfigureAwait(false);

                await using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = $"LISTEN \"{_channel}\";";
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                _isConnected = true;
                backoff = BackoffInitial;
                _logger.LogInformation("Écoute des notifications inter-postes active (canal {Channel})", _channel);

                while (!ct.IsCancellationRequested)
                {
                    await connection.WaitAsync(WaitTimeout, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _isConnected = false;

                if (ct.IsCancellationRequested)
                {
                    break;
                }

                _logger.LogWarning(ex,
                    "Connexion d'écoute perdue — nouvelle tentative dans {Delay}s",
                    backoff.TotalSeconds);

                try
                {
                    await Task.Delay(backoff, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, BackoffMax.TotalSeconds));
            }
            finally
            {
                if (connection is not null)
                {
                    connection.Notification -= OnNotification;
                    _isConnected = false;

                    try
                    {
                        await connection.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // Connexion déjà morte : rien à libérer.
                    }
                }
            }
        }

        _isConnected = false;
        _logger.LogInformation("Écoute des notifications inter-postes arrêtée");
    }

    private void OnNotification(object sender, NpgsqlNotificationEventArgs e)
    {
        if (!string.Equals(e.Channel, _channel, StringComparison.Ordinal))
        {
            return;
        }

        SyncNotificationDto? notification;
        try
        {
            notification = JsonSerializer.Deserialize<SyncNotificationDto>(e.Payload ?? string.Empty);
        }
        catch (JsonException)
        {
            notification = null;
        }

        if (notification is null || string.IsNullOrEmpty(notification.Table))
        {
            _logger.LogWarning("Payload de notification invalide ignoré : {Payload}", e.Payload);
            return;
        }

        if (IsFromSelf(notification))
        {
            return;
        }

        try
        {
            EntityChanged?.Invoke(this, notification);
        }
        catch (Exception ex)
        {
            // Un abonné défaillant ne doit jamais casser la boucle d'écoute.
            _logger.LogError(ex, "Erreur dans un abonné EntityChanged (table {Table})", notification.Table);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
