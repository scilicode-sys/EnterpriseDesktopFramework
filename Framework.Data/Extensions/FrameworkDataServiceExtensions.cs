using Framework.Core.Auth;
using Framework.Core.Data;
using Framework.Core.Sync;
using Framework.Data.Postgres;
using Framework.Data.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Data.Extensions;

/// <summary>Options d'installation du socle données.</summary>
public class FrameworkDataOptions
{
    /// <summary>Canal LISTEN/NOTIFY partagé par tous les postes du produit.</summary>
    public string SyncChannel { get; set; } = "framework_sync";
}

public static class FrameworkDataServiceExtensions
{
    /// <summary>
    /// Enregistre le socle données : resolver de connexion, fabrique de contexte
    /// PostgreSQL résiliente, Unit of Work, service de session et écouteur de
    /// synchronisation. Un projet dérivé remplace FrameworkDbContext par le sien
    /// via la surcharge générique.
    /// </summary>
    public static IServiceCollection AddFrameworkData(
        this IServiceCollection services,
        Action<FrameworkDataOptions>? configure = null)
        => services.AddFrameworkData<FrameworkDbContext>(configure);

    /// <summary>Même enregistrement, avec le contexte propre au projet dérivé.</summary>
    public static IServiceCollection AddFrameworkData<TContext>(
        this IServiceCollection services,
        Action<FrameworkDataOptions>? configure = null)
        where TContext : FrameworkDbContext
    {
        var options = new FrameworkDataOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<PostgresConnectionResolver>();

        // Surcharge (provider, builder) : le resolver est résolu depuis le
        // conteneur définitif, jamais via un BuildServiceProvider temporaire
        // qui dupliquerait les singletons.
        services.AddSingleton<AuditSaveChangesInterceptor>();

        services.AddDbContextFactory<TContext>((provider, builder) =>
        {
            var connectionString = provider
                .GetRequiredService<PostgresConnectionResolver>()
                .ConnectionString;

            builder.UseNpgsql(connectionString, npgsql => npgsql
                .EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null)
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));

            // Une écriture sans auteur n'est pas une écriture traçable : l'audit
            // est posé sur TOUTES les fabriques de contexte, y compris celles
            // d'un produit dérivé. Exige IHorloge (apporté par AddFrameworkCommon).
            builder.AddInterceptors(provider.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        services.AddTransient<IUnitOfWork, UnitOfWork>();

        // Singleton VOLONTAIRE : CurrentUser est l'état de session du poste, pas
        // celui d'une fenêtre. Transitoire, le shell singleton et la fenêtre de
        // connexion auraient chacun leur propre session — l'écran resterait
        // « hors session » après une connexion réussie, et le RBAC refuserait tout.
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<ISyncNotificationService>(provider => new PostgresSyncListener(
            provider.GetRequiredService<PostgresConnectionResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PostgresSyncListener>>(),
            options.SyncChannel));

        return services;
    }
}
