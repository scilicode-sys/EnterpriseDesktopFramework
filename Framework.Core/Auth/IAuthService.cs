namespace Framework.Core.Auth;

/// <summary>
/// Session applicative. Implémentation attendue : anti-force-brute par
/// gel temporaire, horodatage via IHorloge (jamais DateTime.Now en dur).
/// </summary>
public interface IAuthService
{
    AppUser? CurrentUser { get; }

    bool IsAuthenticated { get; }

    Task<AuthenticationResult> LoginAsync(string username, string password, CancellationToken cancellationToken = default);

    Task LogoutAsync();

    /// <summary>Le premier compte du déploiement est créé sans compte existant (amorce).</summary>
    Task<bool> HasAnyAccountAsync(CancellationToken cancellationToken = default);

    /// <summary>Amorce du déploiement : crée le tout premier compte, refusé si la table est déjà peuplée.</summary>
    Task<AuthenticationResult> CreerPremierCompteAsync(
        string username,
        string nomComplet,
        string role,
        string password,
        CancellationToken cancellationToken = default);

    event EventHandler<AppUser?>? AuthenticationStateChanged;
}
