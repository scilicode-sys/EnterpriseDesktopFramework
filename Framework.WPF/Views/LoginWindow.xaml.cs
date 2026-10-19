using System.Windows;
using System.Windows.Controls;
using Framework.WPF.ViewModels;

namespace Framework.WPF.Views;

/// <summary>
/// Fenêtre de connexion. Le mot de passe ne se lie jamais : la PasswordBox
/// le pousse dans le ViewModel à chaque frappe (choix délibéré, pas une omission).
/// </summary>
public partial class LoginWindow : Window
{
    private readonly LoginViewModel _connexion;

    public LoginWindow(LoginViewModel connexion)
    {
        InitializeComponent();

        _connexion = connexion;
        DataContext = connexion;

        ZoneMotDePasse.PasswordChanged += (sender, e) => _connexion.MotDePasse = ZoneMotDePasse.Password;
        ZoneConfirmation.PasswordChanged += (sender, e) => _connexion.Confirmation = ZoneConfirmation.Password;

        Loaded += async (sender, e) => await _connexion.PreparerAsync();

        _connexion.ConnexionReussie += (sender, e) => ((App)Application.Current).OuvrirShell();
    }
}
