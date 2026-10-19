using System.Windows.Controls;
using Framework.WPF.ViewModels;

namespace Framework.WPF.Views;

/// <summary>
/// Vue de l'écran CRUD de référence. Le DataContext vient du ContentControl
/// du shell (template ViewModel → Vue), il n'est jamais injecté au constructeur.
/// </summary>
public partial class ExempleView : UserControl
{
    public ExempleView()
    {
        InitializeComponent();

        Loaded += async (sender, e) =>
        {
            if (DataContext is ExempleViewModel exemple)
            {
                await exemple.ChargerAsync();
            }
        };
    }
}
