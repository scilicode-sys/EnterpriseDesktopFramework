namespace Framework.Core.Notifications;

public enum AlertKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// Alerte visible à l'écran (notification empilée, type « growl »). Une erreur technique ne
/// remonte JAMAIS sa trame brute ici : l'appelant passe une phrase française.
/// </summary>
public interface IAlertService
{
    void Notify(AlertKind kind, string message);

    void Info(string message) => Notify(AlertKind.Info, message);

    void Success(string message) => Notify(AlertKind.Success, message);

    void Warning(string message) => Notify(AlertKind.Warning, message);

    void Error(string message) => Notify(AlertKind.Error, message);
}
