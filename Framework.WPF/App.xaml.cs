using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Framework.Common.Extensions;
using Framework.Common.Telemetry;
using Framework.Common.Update;
using Framework.Core.Navigation;
using Framework.Core.Sync;
using Framework.Core.Theming;
using Framework.Data.Extensions;
using Framework.WPF.Messages;
using Framework.WPF.Services;
using Framework.WPF.ViewModels;
using Framework.WPF.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Framework.WPF;

/// <summary>
/// Composition root du shell : hôte DI (Serilog + Common + Data + WPF),
/// amorçage Velopack en toute première ligne, écouteur inter-postes ponté
/// vers le messager faible (le singleton ne retient jamais un ViewModel).
/// Les fenêtres sont transitoires : une fenêtre WPF fermée ne se rouvre pas.
/// </summary>
public partial class App : Application
{
    /// <summary>Nom de l'installation : dossier %LocalAppData%/%AppData% du produit.</summary>
    public const string NomApplication = "FrameworkDemo";

    private IHost? _hote;
    private ISyncNotificationService? _sync;
    private Window? _fenetreCourante;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Velopack exige d'être la toute première instruction exécutée du poste.
        VelopackUpdateService.DemarrerTantQuePointDentree();

        _hote = Host.CreateDefaultBuilder()
            .UseSerilog((_, configuration) => configuration
                .MinimumLevel.Debug()
                .WriteTo.File(
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        NomApplication, "Logs", "app-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 15))
            .ConfigureServices(services =>
            {
                services.AddFrameworkCommon(NomApplication);
                services.AddFrameworkData();

                services.AddSingleton<NavigationService>();
                services.AddSingleton<INavigationService>(provider =>
                    provider.GetRequiredService<NavigationService>());

                services.AddSingleton<IThemeService, ThemeService>();

                services.AddTransient<LoginViewModel>();
                services.AddTransient<DashboardViewModel>();
                services.AddSingleton<ShellViewModel>();

                services.AddTransient<LoginWindow>();
                services.AddTransient<MainWindow>();
            })
            .Build();

        await _hote.StartAsync();

        _hote.Services.GetRequiredService<IThemeService>().ApplySavedSettings();

        _sync = _hote.Services.GetRequiredService<ISyncNotificationService>();
        _sync.EntityChanged += (_, notification) =>
            WeakReferenceMessenger.Default.Send(new ExternalEntityChangedMessage(notification));
        await _sync.StartAsync();

        OuvrirConnexion();
    }

    /// <summary>Fenêtre de connexion (premier écran, ou retour après fermeture de session).</summary>
    public void OuvrirConnexion()
    {
        BasculerVers(_hote!.Services.GetRequiredService<LoginWindow>());
    }

    /// <summary>Shell principal, appelé par la fenêtre de connexion après un succès.</summary>
    public void OuvrirShell()
    {
        BasculerVers(_hote!.Services.GetRequiredService<MainWindow>());
    }

    private void BasculerVers(Window nouvelle)
    {
        var ancienne = _fenetreCourante;

        // La nouvelle fenêtre est montrée AVANT de fermer l'ancienne :
        // ShutdownMode=OnLastWindowClose éteindrait le poste sur le trou entre les deux.
        _fenetreCourante = nouvelle;
        MainWindow = nouvelle;
        nouvelle.Show();

        ancienne?.Close();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_sync is not null)
        {
            _sync.StopAsync().GetAwaiter().GetResult();
        }

        TelemetrySentry.VidangerAsync().GetAwaiter().GetResult();
        _hote?.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        _hote?.Dispose();

        base.OnExit(e);
    }
}
