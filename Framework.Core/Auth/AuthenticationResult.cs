namespace Framework.Core.Auth;

public enum AuthenticationStatus
{
    Success,
    WrongCredentials,
    Locked,
    Deactivated,
    NoAccount,
    TechnicalFailure,
}

/// <summary>Résultat d'une connexion : statut + message français prêt à l'écran.</summary>
public sealed record AuthenticationResult(AuthenticationStatus Status, string? Message = null)
{
    public bool IsSuccess => Status == AuthenticationStatus.Success;

    public static AuthenticationResult Ok() => new(AuthenticationStatus.Success);
}
