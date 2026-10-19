namespace Framework.Core.Settings;

/// <summary>
/// Réglages locaux clé → valeur, persistés par l'implémentation JSON de
/// Framework.Common (%LocalAppData%\{AppName}\settings.json).
/// </summary>
public interface ISettingsService
{
    T Get<T>(string key, T defaultValue);

    void Set<T>(string key, T value);

    bool TryGet<T>(string key, out T? value);

    bool Has(string key);

    void Delete(string key);
}
