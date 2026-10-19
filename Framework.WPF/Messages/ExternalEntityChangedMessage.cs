using Framework.Core.Sync;

namespace Framework.WPF.Messages;

/// <summary>
/// Message faible porteur d'une notification reçue d'un AUTRE poste.
/// L'App abonne l'écouteur fort (singleton) à ce message faible : les
/// ViewModels transitoires s'abonnent sans jamais être retenus par le service.
/// </summary>
public sealed record ExternalEntityChangedMessage(SyncNotificationDto Notification);
