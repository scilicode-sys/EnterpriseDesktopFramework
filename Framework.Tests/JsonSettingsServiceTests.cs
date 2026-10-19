using System.IO;
using Framework.Common.Settings;
using Framework.Core.Theming;
using Xunit;

namespace Framework.Tests;

/// <summary>
/// Réglages JSON : aller-retour complet (objets, enums), isolation des clés,
/// tolérance à un fichier cabossé, persistance entre deux ouvertures.
/// </summary>
public class JsonSettingsServiceTests : IDisposable
{
    private readonly string _appName;
    private readonly JsonSettingsService _reglages;

    public JsonSettingsServiceTests()
    {
        _appName = $"FrameworkTests-Settings-{Guid.NewGuid():N}";
        _reglages = new JsonSettingsService(_appName);
    }

    public void Dispose()
    {
        var repertoire = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), _appName);

        try
        {
            if (Directory.Exists(repertoire))
            {
                Directory.Delete(repertoire, recursive: true);
            }
        }
        catch (IOException)
        {
            // Propre même si le ménage échoue.
        }
    }

    [Fact]
    public void Get_CleAbsente_RendLeDefaut_SansLEcrire()
    {
        var valeur = _reglages.Get("Inexistante", "défaut");

        Assert.Equal("défaut", valeur);
        Assert.False(_reglages.Has("Inexistante"));
    }

    [Fact]
    public void Set_PuisGet_ObjetComplex_SurvitAuFichier()
    {
        var reglages = new Parametres
        {
            Hote = "db.local",
            Port = 5433,
            Ssl = true,
            Theme = AppTheme.Dark,
        };

        _reglages.Set("Test.Objet", reglages);

        var reelu = new JsonSettingsService(_appName).Get("Test.Objet", new Parametres());

        Assert.Equal(reglages.Hote, reelu.Hote);
        Assert.Equal(reglages.Port, reelu.Port);
        Assert.Equal(reglages.Ssl, reelu.Ssl);
        Assert.Equal(AppTheme.Dark, reelu.Theme);
    }

    [Fact]
    public void Set_DeuxClees_ChacuneResteLisible()
    {
        _reglages.Set("A", 1);
        _reglages.Set("B", "deux");

        Assert.Equal(1, _reglages.Get("A", 0));
        Assert.Equal("deux", _reglages.Get("B", ""));
    }

    [Fact]
    public void Delete_CleConnue_Disparait()
    {
        _reglages.Set("Temporaire", "valeur");
        _reglages.Delete("Temporaire");

        Assert.False(_reglages.Has("Temporaire"));
        Assert.Equal("redefaut", _reglages.Get("Temporaire", "redefaut"));
    }

    [Fact]
    public void FichierCabosse_RedemarreAVide_SansLever()
    {
        File.WriteAllText(_reglages.CheminFichier, "{ ce n'est pas du json");

        var relance = new JsonSettingsService(_appName);

        Assert.Equal("defaut", relance.Get("Rien", "defaut"));

        relance.Set("Reparation", "ok");
        Assert.Equal("ok", relance.Get("Reparation", ""));
    }

    [Fact]
    public void TryGet_TypeIncoherent_RendFalse()
    {
        _reglages.Set("Compteur", 42);

        Assert.False(_reglages.TryGet<bool>("Compteur", out var booleen));
        Assert.False(booleen);
    }

    private sealed class Parametres
    {
        public string Hote { get; set; } = "";
        public int Port { get; set; }
        public bool Ssl { get; set; }
        public AppTheme Theme { get; set; }
    }
}
