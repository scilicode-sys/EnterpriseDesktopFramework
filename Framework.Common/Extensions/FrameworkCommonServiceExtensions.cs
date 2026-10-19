using Framework.Common.Notifications;
using Framework.Common.Security;
using Framework.Common.Settings;
using Framework.Common.Time;
using Framework.Core.Notifications;
using Framework.Core.Settings;
using Framework.Core.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Common.Extensions;

public static class FrameworkCommonServiceExtensions
{
    /// <summary>
    /// Enregistre les services transverses du framework. <paramref name="appName"/>
    /// nomme le dossier de l'application sous %LocalAppData% / %AppData% :
    /// c'est l'identité de l'installation, chaque produit dérivé passe la sienne.
    /// Velopack et Sentry s'amorcent explicitement dans l'App (ils ont besoin
    /// d'un feed et d'un DSN propres au produit), pas ici.
    /// </summary>
    public static IServiceCollection AddFrameworkCommon(
        this IServiceCollection services,
        string appName)
    {
        services.AddSingleton<IHorloge, SystemHorloge>();

        services.AddSingleton<ISettingsService>(new JsonSettingsService(appName));

        services.AddSingleton(new EncryptionService(appName));

        services.AddSingleton<GrowlAlertService>();
        services.AddSingleton<IAlertService>(provider => provider.GetRequiredService<GrowlAlertService>());

        return services;
    }
}
