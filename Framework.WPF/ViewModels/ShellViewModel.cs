using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Framework.Core.Auth;
using Framework.Core.Navigation;
using Framework.Core.Sync;
using Framework.Core.Theming;
using Framework.WPF.Messages;
using Framework.WPF.Models;
using Framework.WPF.Services;

namespace Framework.WPF.ViewModels;

/// <summary>
/// ViewModel du shell : barre latérale (rétractable), thème, session,
/// état de la synchronisation inter-postes. C'est le seul ViewModel
/// singleton — les écrans restent transitoires.
/// </summary>
public partial class ShellViewModel : ObservableObject, IRecipient<ExternalEntityChangedMessage>
{
    private const double LargeurDepliee = 230;
    private const double LargeurRepliee = 56;

    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly IAuthService _auth;

    public IReadOnlyList<NavigationItem> Items { get; }

    [ObservableProperty]
    private NavigationItem? _itemSelectionne;

    [ObservableProperty]
    private bool _sidebarDepliee = true;

    [ObservableProperty]
    private double _largeurSidebar = LargeurDepliee;

    [ObservableProperty]
    private object? _viewModelCourant;

    [ObservableProperty]
    private string _nomUtilisateur = string.Empty;

    [ObservableProperty]
    private string _roleUtilisateur = string.Empty;

    [ObservableProperty]
    private string _etatSynchronisation = "Écoute inter-postes en cours";

    [ObservableProperty]
    private string _titreCourant = "Tableau de bord";

    [ObservableProperty]
    private bool _canRetour;

    [ObservableProperty]
    private int _compteurNotifications;

    public ShellViewModel(
        NavigationService navigation,
        IThemeService theme,
        IAuthService auth)
    {
        _navigation = navigation;
        _theme = theme;
        _auth = auth;

        Items = new[]
        {
            new NavigationItem("Tableau de bord", "", typeof(DashboardViewModel)),
        };

        _navigation.CurrentViewModelChanged += (_, type) =>
        {
            ViewModelCourant = navigation.ViewModelCourant;
            CanRetour = navigation.CanGoBack;
            synchroniserSelection(type);
        };

        _auth.AuthenticationStateChanged += (_, utilisateur) => afficherUtilisateur(utilisateur);
        afficherUtilisateur(_auth.CurrentUser);

        WeakReferenceMessenger.Default.Register(this);

        // Écran initial du shell.
        _navigation.Reset(typeof(DashboardViewModel));
        ItemSelectionne = Items[0];
    }

    public void Receive(ExternalEntityChangedMessage message)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            CompteurNotifications++;
            EtatSynchronisation = $"Dernière notification : {message.Notification.Table} ({message.Notification.Operation})";
        });
    }

    [RelayCommand]
    private void Naviguer(NavigationItem? item)
    {
        if (item is not null)
        {
            _navigation.NavigateTo(item.Cible);
        }
    }

    partial void OnItemSelectionneChanged(NavigationItem? value)
    {
        if (value is not null && _navigation.CurrentViewModelType != value.Cible)
        {
            _navigation.NavigateTo(value.Cible);
        }
    }

    [RelayCommand]
    private void BasculerSidebar()
    {
        SidebarDepliee = !SidebarDepliee;
        LargeurSidebar = SidebarDepliee ? LargeurDepliee : LargeurRepliee;
    }

    [RelayCommand]
    private void BasculerTheme() => _theme.ToggleTheme();

    [RelayCommand]
    private async Task FermerSessionAsync()
    {
        await _auth.LogoutAsync();
        ((App)System.Windows.Application.Current).OuvrirConnexion();
    }

    [RelayCommand]
    private void Revenir()
    {
        if (_navigation.CanGoBack)
        {
            _navigation.GoBack();
        }
    }

    private void synchroniserSelection(Type? type)
    {
        var correspondant = Items.FirstOrDefault(item => item.Cible == type);

        if (correspondant is not null && ItemSelectionne != correspondant)
        {
            ItemSelectionne = correspondant;
        }

        if (correspondant is not null)
        {
            TitreCourant = correspondant.Libelle;
        }
    }

    private void afficherUtilisateur(AppUser? utilisateur)
    {
        NomUtilisateur = utilisateur?.NomComplet ?? "(hors session)";
        RoleUtilisateur = utilisateur?.Role ?? string.Empty;
    }
}
