using System.Windows;
using Framework.WPF.ViewModels;

namespace Framework.WPF.Views;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel shell)
    {
        InitializeComponent();
        DataContext = shell;
    }
}
