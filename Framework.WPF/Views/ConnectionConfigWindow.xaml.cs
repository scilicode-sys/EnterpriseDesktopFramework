using System.ComponentModel;
using System.Windows;
using Framework.WPF.ViewModels;

namespace Framework.WPF.Views;

/// <summary>
/// Fenêtre autonome de configuration réseau, ouverte au premier lancement ou
/// depuis l'écran de connexion. Le mot de passe ne se lie jamais : la
/// PasswordBox le pousse dans le ViewModel à chaque frappe. La fenêtre se ferme
/// d'elle-même dès que l'enregistrement a réussi.
/// </summary>
public partial class ConnectionConfigWindow : Window
{
    private readonly ConnectionConfigViewModel _configuration;

    public ConnectionConfigWindow(ConnectionConfigViewModel configuration)
    {
        InitializeComponent();

        _configuration = configuration;
        DataContext = configuration;

        ZoneMotDePasse.PasswordChanged += (sender, e) => _configuration.MotDePasse = ZoneMotDePasse.Password;

        _configuration.PropertyChanged += OnConfigurationPropertyChanged;
    }

    private void OnConfigurationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionConfigViewModel.EstConfigure) && _configuration.EstConfigure)
        {
            // Le temps que le poste affiche « réglages enregistrés » en entier,
            // puis la fenêtre autonome se ferme d'elle-même.
            Task.Delay(TimeSpan.FromSeconds(1.5))
                .ContinueWith(_ =>
                {
                    if (IsVisible)
                    {
                        Dispatcher.Invoke(Close);
                    }
                }, TaskScheduler.Default);
        }
    }
}
