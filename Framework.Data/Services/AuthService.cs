using Framework.Core.Auth;
using Framework.Core.Security;
using Framework.Core.Time;
using Framework.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Framework.Data.Services;

/// <summary>
/// Authentification de session avec anti-force-brute : trois échecs consecutifs
/// gèlent le compte soixante secondes. Toute lecture du temps passe par IHorloge.
/// Transient dans la DI : jamais de session partagée par accident entre deux fenêtres.
/// </summary>
public sealed class AuthService : IAuthService
{
    public const int MaxEchecsAvantBlocage = 3;
    public static readonly TimeSpan DureeBlocage = TimeSpan.FromSeconds(60);

    private readonly IDbContextFactory<FrameworkDbContext> _contextFactory;
    private readonly IHorloge _horloge;
    private readonly ILogger<AuthService> _logger;

    public AppUser? CurrentUser { get; private set; }

    public bool IsAuthenticated => CurrentUser is not null;

    public event EventHandler<AppUser?>? AuthenticationStateChanged;

    public AuthService(
        IDbContextFactory<FrameworkDbContext> contextFactory,
        IHorloge horloge,
        ILogger<AuthService> logger)
    {
        _contextFactory = contextFactory;
        _horloge = horloge;
        _logger = logger;
    }

    public async Task<AuthenticationResult> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var compte = await context.Comptes
            .FirstOrDefaultAsync(c => c.Username == username, cancellationToken);

        if (compte is null)
        {
            _logger.LogWarning("Connexion refusée : compte « {Username} » inconnu.", username);
            return new AuthenticationResult(AuthenticationStatus.WrongCredentials,
                "Identifiant ou mot de passe incorrect.");
        }

        if (!compte.Actif)
        {
            _logger.LogWarning("Connexion refusée : le compte « {Username} » est désactivé.", username);
            return new AuthenticationResult(AuthenticationStatus.Deactivated,
                $"Connexion refusée : le compte « {username} » est désactivé.");
        }

        var maintenant = _horloge.Maintenant;

        if (compte.GelJusqua is { } gel && gel > maintenant)
        {
            var restant = (int)Math.Ceiling((gel - maintenant).TotalSeconds);
            _logger.LogWarning("Connexion refusée : compte « {Username} » gelé jusqu'à {Gel:HH:mm:ss}.", username, gel);
            return new AuthenticationResult(AuthenticationStatus.Locked,
                $"Connexion refusée : compte « {username} » gelé jusqu'à {gel:HH:mm:ss}.");
        }

        if (!PasswordHasher.Verify(password, compte.PasswordHash))
        {
            await RegistrerEchecAsync(context, compte, maintenant, cancellationToken);
            return new AuthenticationResult(AuthenticationStatus.WrongCredentials,
                "Identifiant ou mot de passe incorrect.");
        }

        compte.EchecsConsecutifs = 0;
        compte.GelJusqua = null;
        await context.SaveChangesAsync(cancellationToken);

        CurrentUser = new AppUser(compte.Id, compte.Username, compte.NomComplet, compte.Role, compte.Actif);
        _logger.LogInformation("Session ouverte : « {Username} » (rôle {Role}).", compte.Username, compte.Role);
        AuthenticationStateChanged?.Invoke(this, CurrentUser);

        return AuthenticationResult.Ok();
    }

    public Task LogoutAsync()
    {
        var ancien = CurrentUser;
        CurrentUser = null;

        if (ancien is not null)
        {
            _logger.LogInformation("Session fermée : « {Username} ».", ancien.Username);
        }

        AuthenticationStateChanged?.Invoke(this, null);
        return Task.CompletedTask;
    }

    public async Task<bool> HasAnyAccountAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Comptes.AnyAsync(cancellationToken);
    }

    /// <summary>
    /// Amorce du déploiement : crée le tout premier compte, uniquement si la
    /// table est vide. À appeler par l'écran de premier lancement du poste.
    /// </summary>
    public async Task<AuthenticationResult> CreerPremierCompteAsync(
        string username,
        string nomComplet,
        string role,
        string password,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        if (await context.Comptes.AnyAsync(cancellationToken))
        {
            return new AuthenticationResult(AuthenticationStatus.TechnicalFailure,
                "Des comptes existent déjà : la création du premier compte est fermée.");
        }

        context.Comptes.Add(new CompteEntity
        {
            Username = username,
            NomComplet = nomComplet,
            Role = role,
            PasswordHash = PasswordHasher.Hash(password),
            Actif = true,
        });

        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Premier compte créé : « {Username} » (rôle {Role}).", username, role);

        return AuthenticationResult.Ok();
    }

    private async Task RegistrerEchecAsync(
        FrameworkDbContext context,
        CompteEntity compte,
        DateTime maintenant,
        CancellationToken cancellationToken)
    {
        compte.EchecsConsecutifs++;

        if (compte.EchecsConsecutifs >= MaxEchecsAvantBlocage)
        {
            compte.GelJusqua = maintenant.Add(DureeBlocage);
            compte.EchecsConsecutifs = 0;
            _logger.LogWarning(
                "Compte « {Username} » gelé jusqu'à {Gel:HH:mm:ss} après {Max} échecs.",
                compte.Username, compte.GelJusqua, MaxEchecsAvantBlocage);
        }
        else
        {
            _logger.LogWarning(
                "Mot de passe incorrect pour « {Username} » ({Echec}/{Max}).",
                compte.Username, compte.EchecsConsecutifs, MaxEchecsAvantBlocage);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
