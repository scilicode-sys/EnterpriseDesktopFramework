using Framework.Core.Auth;

namespace Framework.Tests;

/// <summary>
/// Session de test : le rôle est posé à la main, sans base ni mot de passe.
/// La chaîne d'authentification (hash, anti-force-brute) reste l'affaire de
/// AuthServiceTests ; ici on ne teste que ce que la matrice en déduit.
/// </summary>
internal sealed class FakeSession : IAuthService
{
    public AppUser? CurrentUser { get; private set; }

    public bool IsAuthenticated => CurrentUser is not null;

    public event EventHandler<AppUser?>? AuthenticationStateChanged;

    public FakeSession(string? role = null)
    {
        if (role is not null)
        {
            SeConnecter(role);
        }
    }

    public void SeConnecter(string role)
    {
        CurrentUser = new AppUser(Guid.NewGuid(), "agent.test", "Agent Test", role, true);
        AuthenticationStateChanged?.Invoke(this, CurrentUser);
    }

    public void FermerSession()
    {
        CurrentUser = null;
        AuthenticationStateChanged?.Invoke(this, null);
    }

    public Task<AuthenticationResult> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
        => Task.FromResult(AuthenticationResult.Ok());

    public Task LogoutAsync()
    {
        FermerSession();
        return Task.CompletedTask;
    }

    public Task<bool> HasAnyAccountAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<AuthenticationResult> CreerPremierCompteAsync(
        string username,
        string nomComplet,
        string role,
        string password,
        CancellationToken cancellationToken = default)
        => Task.FromResult(AuthenticationResult.Ok());
}
