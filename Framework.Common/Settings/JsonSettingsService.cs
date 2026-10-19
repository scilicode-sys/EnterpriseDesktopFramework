using System.IO;
using Framework.Core.Settings;
using System.Text.Json;

namespace Framework.Common.Settings;

/// <summary>
/// Réglages locaux : un fichier JSON plat (%LocalAppData%\{AppName}\settings.json),
/// rechargé et resauvegardé entier sous verrou. Les valeurs sont stockées en
/// JsonElement et redésérialisées à la lecture — enums compris via leur nom.
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions LectureOptions = new(JsonSerializerDefaults.Web);

    private readonly object _lock = new();
    private readonly string _cheminFichier;
    private Dictionary<string, JsonElement> _racine;

    public JsonSettingsService(string appName)
    {
        var repertoire = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName);

        Directory.CreateDirectory(repertoire);
        _cheminFichier = Path.Combine(repertoire, "settings.json");

        _racine = Charger();
    }

    public string CheminFichier => _cheminFichier;

    public T Get<T>(string key, T defaultValue)
    {
        if (TryGet(key, out T? value) && value is not null)
        {
            return value;
        }

        // Le défaut n'est jamais réécrit en base de réglages : un réglage
        // absent le reste, et l'assistant peut détecter son absence via Has().
        return defaultValue;
    }

    public void Set<T>(string key, T value)
    {
        lock (_lock)
        {
            _racine[key] = JsonSerializer.SerializeToElement(value, LectureOptions);
            Sauvegarder();
        }
    }

    public bool TryGet<T>(string key, out T? value)
    {
        value = default;

        JsonElement element;
        lock (_lock)
        {
            if (!_racine.TryGetValue(key, out element))
            {
                return false;
            }
        }

        try
        {
            value = element.Deserialize<T>(LectureOptions);
            return value is not null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Type enregistré incompatible avec le type demandé : la clé est
            // traitée comme absente, le défaut de l'appelant reprend la main.
            return false;
        }
    }

    public bool Has(string key)
    {
        lock (_lock)
        {
            return _racine.ContainsKey(key);
        }
    }

    public void Delete(string key)
    {
        lock (_lock)
        {
            if (_racine.Remove(key))
            {
                Sauvegarder();
            }
        }
    }

    private Dictionary<string, JsonElement> Charger()
    {
        try
        {
            if (!File.Exists(_cheminFichier))
            {
                return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            }

            using var document = JsonDocument.Parse(File.ReadAllText(_cheminFichier));

            var dictionnaire = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var propriete in document.RootElement.EnumerateObject())
            {
                dictionnaire[propriete.Name] = propriete.Value.Clone();
            }

            return dictionnaire;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Un fichier cabossé ne doit jamais empêcher le poste de démarrer :
            // on repart vide et la prochaine écriture le répare.
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }
    }

    private void Sauvegarder()
    {
        var temporaires = _cheminFichier + ".tmp";
        File.WriteAllText(temporaires, JsonSerializer.Serialize(_racine, LectureOptions));
        File.Move(temporaires, _cheminFichier, overwrite: true);
    }
}
