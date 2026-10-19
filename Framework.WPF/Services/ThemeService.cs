using System.IO;
using System.Windows;
using System.Windows.Media;
using Framework.Core.Settings;
using Framework.Core.Theming;
using Microsoft.Extensions.Logging;

namespace Framework.WPF.Services;

/// <summary>
/// Thème appliqué à chaud : les deux palettes sont des dictionnaires fusionnés
/// de l'application, remplacés l'un par l'autre sans redessin des fenêtres
/// (toute consommation est en DynamicResource). L'accent est posé en clé directe
/// au niveau application : il gagne sur le dictionnaire de palette et survit à ses swaps.
/// </summary>
public sealed class ThemeService : IThemeService
{
    public const string CleTheme = "Theme.AppTheme";
    public const string CleCouleurAccente = "Theme.AccenteColor";

    public const string AccenteParDefaut = "#0E9384";

    private static readonly Uri UriClair = new("/Resources/Theme/Light.xaml", UriKind.Relative);
    private static readonly Uri UriSombre = new("/Resources/Theme/Dark.xaml", UriKind.Relative);

    private readonly ISettingsService _settings;
    private readonly ILogger<ThemeService> _logger;

    public AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

    public string AccentColor { get; private set; } = AccenteParDefaut;

    public event EventHandler<AppTheme>? ThemeChanged;

    public ThemeService(ISettingsService settings, ILogger<ThemeService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public void ApplySavedSettings()
    {
        var theme = _settings.Get(CleTheme, AppTheme.Light);
        AccentColor = _settings.Get(CleCouleurAccente, AccenteParDefaut);

        AppliquerTheme(theme, enregistrer: false);
        AppliquerAccente();
    }

    public void SetTheme(AppTheme theme)
    {
        AppliquerTheme(theme, enregistrer: true);
    }

    public void ToggleTheme()
        => SetTheme(CurrentTheme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);

    public void SetAccentColor(string hexColor)
    {
        AccentColor = hexColor;
        AppliquerAccente();
        _settings.Set(CleCouleurAccente, hexColor);
    }

    private void AppliquerTheme(AppTheme theme, bool enregistrer)
    {
        var ressources = Application.Current.Resources;
        var cible = theme == AppTheme.Dark ? UriSombre : UriClair;

        var dictionnaireCourant = trouverDictionnaireDePalette(ressources);
        var nouveau = new ResourceDictionary { Source = cible };

        if (dictionnaireCourant is null)
        {
            ressources.MergedDictionaries.Add(nouveau);
        }
        else
        {
            ressources.MergedDictionaries[ressources.MergedDictionaries.IndexOf(dictionnaireCourant)] = nouveau;
        }

        CurrentTheme = theme;

        // L'accent direct au niveau application a été posé avant ce swap :
        // le re-poser garantit qu'il reste au-dessus de la nouvelle palette.
        AppliquerAccente();

        if (enregistrer)
        {
            _settings.Set(CleTheme, theme);
        }

        _logger.LogInformation("Thème appliqué : {Theme}.", theme);
        ThemeChanged?.Invoke(this, theme);
    }

    private void AppliquerAccente()
    {
        try
        {
            var couleur = (Color)ColorConverter.ConvertFromString(AccentColor);
            var brosse = new SolidColorBrush(couleur);
            brosse.Freeze();
            Application.Current.Resources["Brush.Accente"] = brosse;
        }
        catch (FormatException)
        {
            _logger.LogWarning("Couleur d'accent illisible ({Accent}) : accent par défaut rendu.", AccentColor);
            Application.Current.Resources["Brush.Accente"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(AccenteParDefaut));
        }
    }

    private static ResourceDictionary? trouverDictionnaireDePalette(ResourceDictionary ressources)
    {
        foreach (var dictionnaire in ressources.MergedDictionaries)
        {
            var source = dictionnaire.Source?.OriginalString;

            if (source is not null && source.Contains("/Resources/Theme/", StringComparison.Ordinal)
                && !source.Contains("Controls", StringComparison.Ordinal))
            {
                return dictionnaire;
            }
        }

        return null;
    }
}
