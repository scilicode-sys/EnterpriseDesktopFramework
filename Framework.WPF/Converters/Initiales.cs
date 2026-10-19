using System.Globalization;
using System.Windows.Data;

namespace Framework.WPF.Converters;

/// <summary>Initiales d'un nom complet (« Amine Bouchentouf » → « AB ») pour l'avatar.</summary>
public sealed class Initiales : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var mots = (value as string ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (mots.Length == 0)
        {
            return "?";
        }

        return new string(mots.Take(2).Select(mot => char.ToUpperInvariant(mot[0])).ToArray());
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
