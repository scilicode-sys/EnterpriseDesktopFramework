using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Framework.Core.Time;
using Framework.WPF.Messages;

namespace Framework.WPF.ViewModels;

/// <summary>
/// Écran de démonstration du shell : il consomme le messager faible alimenté
/// par l'écouteur LISTEN/NOTIFY. C'est le modèle d'écran CRUD que le projet
/// dérivé remplacera (IUnitOfWork.Repository&lt;TEntity&gt;() est déjà là pour ça).
/// </summary>
public partial class DashboardViewModel : ObservableObject, IRecipient<ExternalEntityChangedMessage>
{
    private readonly IHorloge _horloge;

    [ObservableProperty]
    private string _titre = "Tableau de bord";

    [ObservableProperty]
    private int _notificationsRecues;

    [ObservableProperty]
    private string _derniereActivite = "Aucune activité d'un autre poste depuis l'ouverture.";

    public DashboardViewModel(IHorloge horloge)
    {
        _horloge = horloge;
        WeakReferenceMessenger.Default.Register(this);
    }

    public void Receive(ExternalEntityChangedMessage message)
    {
        var horodatage = _horloge.Maintenant;

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            NotificationsRecues++;
            DerniereActivite = $"Table « {message.Notification.Table} » — opération « {message.Notification.Operation} » à {horodatage:HH:mm:ss}.";
        });
    }
}
