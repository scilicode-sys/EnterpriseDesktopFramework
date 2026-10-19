using Framework.Common.Licensing;
using Framework.Common.Notifications;
using Framework.Common.Reporting;
using Framework.Common.Security;
using Framework.Common.Settings;
using Framework.Common.Time;
using Framework.Core.Licensing;
using Framework.Core.Notifications;
using Framework.Core.Settings;
using Framework.Core.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Common.Extensions;

public static class FrameworkCommonServiceExtensions
{
    /// <summary>
    /// Enregistre les services transverses du framework. <paramref name="appName"/>
    /// nomme le dossier de l'installation (%LocalAppData% / %AppData% / %ProgramData%) :
    /// c'est l'identité du produit, chaque projet dérivé passe la sienne.
    /// <paramref name="licenseSecretKey"/> active le module licence : sans clé,
    /// ILicenseService n'est pas enregistrée (le produit choisit de ne pas licencier).
    /// Velopack et Sentry s'amorcent explicitement dans l'App (ils ont besoin
    /// d'un feed et d'un DSN propres au produit), pas ici.
    /// </summary>
    public static IServiceCollection AddFrameworkCommon(
        this IServiceCollection services,
        string appName,
        string? licenseSecretKey = null)
    {
        services.AddSingleton<IHorloge, SystemHorloge>();

        services.AddSingleton<ISettingsService>(new JsonSettingsService(appName));

        services.AddSingleton(new EncryptionService(appName));

        services.AddSingleton<GrowlAlertService>();
        services.AddSingleton<IAlertService>(provider => provider.GetRequiredService<GrowlAlertService>());

        services.AddSingleton<HwidProvider>();
        services.AddSingleton<PdfReportService>();

        if (!string.IsNullOrEmpty(licenseSecretKey))
        {
            services.AddSingleton<ILicenseService>(provider => new LicenseService(
                provider.GetRequiredService<HwidProvider>(),
                provider.GetRequiredService<IHorloge>(),
                provider.GetRequiredService<ISettingsService>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LicenseService>>(),
                appName,
                licenseSecretKey));
        }

        return services;
    }
}
