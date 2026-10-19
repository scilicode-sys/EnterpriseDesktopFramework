using System.Globalization;
using System.Windows.Data;

namespace Framework.WPF.Converters;

public sealed class InverseBooleen : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;
}
