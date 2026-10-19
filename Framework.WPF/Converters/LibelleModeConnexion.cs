using System.Globalization;
using System.Windows.Data;

namespace Framework.WPF.Converters;

/// <summary>Le mode de la fenêtre se lit dans son titre : premier compte ou connexion.</summary>
public sealed class LibelleModeConnexion : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "Créer le premier compte" : "Connexion";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
