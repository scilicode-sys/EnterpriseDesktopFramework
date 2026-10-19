namespace Framework.Core.Theming;

/// <summary>
/// Thème d'application appliqué à chaud : chaque clé de ressource doit être
/// consommée en DynamicResource pour que le changement soit visible sans redessin.
/// </summary>
public interface IThemeService
{
    AppTheme CurrentTheme { get; }

    string AccentColor { get; }

    void ApplySavedSettings();

    void SetTheme(AppTheme theme);

    void ToggleTheme();

    void SetAccentColor(string hexColor);

    event EventHandler<AppTheme>? ThemeChanged;
}
