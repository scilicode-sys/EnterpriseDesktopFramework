using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Framework.WPF.Converters;

public sealed class BooleenVersVisibilite : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}
