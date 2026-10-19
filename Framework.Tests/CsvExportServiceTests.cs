using System.IO;
using System.Text;
using Framework.Common.Reporting;
using Xunit;

namespace Framework.Tests;

/// <summary>Export CSV universel : échappement RFC 4180, formats français, BOM Excel.</summary>
public class CsvExportServiceTests : IDisposable
{
    private readonly string _repertoire;

    public CsvExportServiceTests()
    {
        _repertoire = Path.Combine(Path.GetTempPath(), $"FrameworkTests-Csv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_repertoire);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_repertoire, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Ligne_SéparateurPointVirgule_ParDéfaut()
    {
        var ligne = CsvExportService.Ligne(["Nom", "Ville"], new CsvOptions());

        Assert.Equal("Nom;Ville", ligne);
    }

    [Theory]
    [InlineData("Simple", "Simple")]
    [InlineData("A;B", "\"A;B\"")]
    [InlineData("Il dit \"bonjour\"", "\"Il dit \"\"bonjour\"\"\"")]
    [InlineData("Ligne1\nLigne2", "\"Ligne1\nLigne2\"")]
    [InlineData(" espacé", "\" espacé\"")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Echapper_Rfc4180(string? champ, string attendu)
    {
        Assert.Equal(attendu, CsvExportService.Echapper(champ, ';'));
    }

    [Fact]
    public void Formater_ValeursFrançaisStables()
    {
        var options = new CsvOptions();

        Assert.Equal("Oui", CsvExportService.Formater(true, options));
        Assert.Equal("Non", CsvExportService.Formater(false, options));
        Assert.Equal("01/07/2026 09:30", CsvExportService.Formater(new DateTime(2026, 7, 1, 9, 30, 0), options));
        Assert.Equal("15/06/2026", CsvExportService.Formater(new DateOnly(2026, 6, 15), options));
        Assert.Equal("1234.56", CsvExportService.Formater(1234.56m, options));
        Assert.Equal("09:45", CsvExportService.Formater(new TimeOnly(9, 45), options));
        Assert.Equal(string.Empty, CsvExportService.Formater(null, options));
    }

    [Fact]
    public void Exporter_Réflexion_TetesColonnesEtLignes()
    {
        var chemin = Chemin("eleves.csv");

        CsvExportService.Exporter(new[]
        {
            new Eleve { Nom = "Amara", Téléphone = "06 12 34 56 78" },
            new Eleve { Nom = "B;en", Téléphone = null },
        }, chemin);

        var lignes = File.ReadAllLines(chemin, Encoding.UTF8);

        Assert.Equal("Nom;Téléphone", lignes[0]);
        Assert.Equal("Amara;06 12 34 56 78", lignes[1]);
        Assert.Equal("\"B;en\";", lignes[2]);
    }

    [Fact]
    public void Exporter_BOMUtf8_OuvertureExcelSansMoisissure()
    {
        var chemin = Chemin("bom.csv");

        CsvExportService.Exporter(
            (IReadOnlyList<string>)["a"],
            new[] { (IReadOnlyList<string>)["b"] },
            chemin);

        var octets = File.ReadAllBytes(chemin);

        Assert.Equal(0xEF, octets[0]);
        Assert.Equal(0xBB, octets[1]);
        Assert.Equal(0xBF, octets[2]);
    }

    [Fact]
    public void Exporter_SéparateurVirgule_RespecteLeChoix()
    {
        var chemin = Chemin("virgule.csv");

        CsvExportService.Exporter(["a", "b"], new[] { (IReadOnlyList<string>)["1", "2,3"] }, chemin, new CsvOptions { Separateur = ',' });

        Assert.Equal("a,b", File.ReadLines(chemin, Encoding.UTF8).First());
        Assert.Equal("1,\"2,3\"", File.ReadLines(chemin, Encoding.UTF8).ElementAt(1));
    }

    private string Chemin(string nom) => Path.Combine(_repertoire, nom);

    private sealed class Eleve
    {
        public string Nom { get; set; } = "";
        public string? Téléphone { get; set; }
    }
}
