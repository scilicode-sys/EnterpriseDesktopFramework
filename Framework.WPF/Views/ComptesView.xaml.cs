using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Framework.WPF.ViewModels;

namespace Framework.WPF.Views;

/// <summary>
/// Vue « Comptes ». Le mot de passe ne se lie jamais : la PasswordBox le pousse
/// dans le ViewModel à chaque frappe, et le ViewModel rappelle la vue quand il
/// faut vider les champs (sélection d'une autre ligne, formulaire nouveau).
/// </summary>
public partial class ComptesView : UserControl
{
    private ComptesViewModel? _ecran;

    public ComptesView()
    {
        InitializeComponent();

        ZoneMotDePasse.PasswordChanged += ZoneMotDePasse_PasswordChanged;
        ZoneConfirmation.PasswordChanged += ZoneConfirmation_PasswordChanged;

        DataContextChanged += (sender, e) => brancherViewModel();

        Loaded += async (sender, e) =>
        {
            if (DataContext is ComptesViewModel comptes)
            {
                await comptes.ChargerAsync();
            }
        };
    }

    /// <summary>Le shell recycle la vue par template : le ViewModel change sous elle sans que la vue se reconstruise.</summary>
    private void brancherViewModel()
    {
        if (ReferenceEquals(_ecran, DataContext) || DataContext is not ComptesViewModel comptes)
        {
            return;
        }

        if (_ecran is not null)
        {
            _ecran.PropertyChanged -= ecran_PropertyChanged;
        }

        _ecran = comptes;
        _ecran.PropertyChanged += ecran_PropertyChanged;
    }

    private void ZoneMotDePasse_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_ecran is not null)
        {
            _ecran.MotDePasse = ZoneMotDePasse.Password;
        }
    }

    private void ZoneConfirmation_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_ecran is not null)
        {
            _ecran.Confirmation = ZoneConfirmation.Password;
        }
    }

    /// <summary>
    /// Le ViewModel tient le mot de passe mais ne peut pas écrire dans une
    /// PasswordBox : c'est la porte de sortie. Sans elle, le champ afficherait
    /// encore le mot de passe du compte précédemment sélectionné.
    /// </summary>
    private void ecran_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ComptesViewModel.GenerationMotDePasse))
        {
            return;
        }

        // Clear() déclenche PasswordChanged : le ViewModel reçoit une chaîne vide,
        // ce qui est exactement l'état qu'il vient de choisir.
        ZoneMotDePasse.Clear();
        ZoneConfirmation.Clear();
    }
}
