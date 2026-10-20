using System.Windows.Controls;
using Framework.WPF.ViewModels;

namespace Framework.WPF.Views;

/// <summary>Vue de la matrice des rôles ; le DataContext vient du shell, jamais du constructeur.</summary>
public partial class DroitsView : UserControl
{
    public DroitsView()
    {
        InitializeComponent();

        Loaded += async (sender, e) =>
        {
            if (DataContext is DroitsViewModel droits)
            {
                await droits.ChargerAsync();
            }
        };
    }
}
