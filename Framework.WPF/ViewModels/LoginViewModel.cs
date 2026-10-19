using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Framework.Core.Auth;
using Framework.Core.Notifications;
using Framework.Core.Rbac;

namespace Framework.WPF.ViewModels;

/// <summary>
/// Connexion + amorce du déploiement : si aucune table de comptes n'est peuplée,
/// le même écran devient création du premier compte. Le mot de passe arrive par
/// le code-behind (PasswordBox), jamais par une liaison.
/// </summary>
public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _auth;
    private readonly IAlertService _alertes;

    [ObservableProperty]
    private string _identifiant = string.Empty;

    [ObservableProperty]
    private string _motDePasse = string.Empty;

    [ObservableProperty]
    private string _nomComplet = string.Empty;

    [ObservableProperty]
    private string _confirmation = string.Empty;

    [ObservableProperty]
    private bool _modePremierCompte;

    [ObservableProperty]
    private bool _estOccupe;

    [ObservableProperty]
    private string? _messageErreur;

    public event EventHandler? ConnexionReussie;

    public LoginViewModel(IAuthService auth, IAlertService alertes)
    {
        _auth = auth;
        _alertes = alertes;
    }

    /// <summary>Appelé à l'ouverture de la fenêtre : décide du mode premier compte.</summary>
    public async Task PreparerAsync()
    {
        try
        {
            ModePremierCompte = !await _auth.HasAnyAccountAsync();
        }
        catch (Exception ex)
        {
            MessageErreur = "Connexion au serveur de données impossible. Vérifiez la configuration réseau de ce poste.";
            _alertes.Error(ex.Message);
        }
    }

    [RelayCommand]
    private async Task SeConnecterAsync()
    {
        if (EstOccupe)
        {
            return;
        }

        MessageErreur = null;

        if (string.IsNullOrWhiteSpace(Identifiant) || string.IsNullOrWhiteSpace(MotDePasse))
        {
            MessageErreur = "Identifiant et mot de passe sont requis.";
            return;
        }

        try
        {
            EstOccupe = true;

            AuthenticationResult resultat;

            if (ModePremierCompte)
            {
                if (string.IsNullOrWhiteSpace(NomComplet))
                {
                    MessageErreur = "Le nom complet est requis pour le premier compte.";
                    return;
                }

                if (MotDePasse != Confirmation)
                {
                    MessageErreur = "La confirmation ne correspond pas au mot de passe.";
                    return;
                }

                resultat = await _auth.CreerPremierCompteAsync(
                    Identifiant.Trim(), NomComplet.Trim(), MatriceParDefaut.Administrateur, MotDePasse);

                if (!resultat.IsSuccess)
                {
                    MessageErreur = resultat.Message;
                    return;
                }

                ModePremierCompte = false;
            }

            resultat = await _auth.LoginAsync(Identifiant.Trim(), MotDePasse);

            if (resultat.IsSuccess)
            {
                ConnexionReussie?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                MessageErreur = resultat.Message ?? "Connexion refusée.";
            }
        }
        catch (Exception)
        {
            MessageErreur = "Connexion au serveur de données impossible. Réessayez ou vérifiez la configuration du poste.";
        }
        finally
        {
            EstOccupe = false;
        }
    }
}
