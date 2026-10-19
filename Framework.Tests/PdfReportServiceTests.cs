using System.IO;
using System.Text;
using Framework.Common.Reporting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Framework.Tests;

/// <summary>Génération PDF : le modèle neutre doit sortir un fichier %PDF lisible.</summary>
public class PdfReportServiceTests : IDisposable
{
    private readonly string _repertoire;
    private readonly PdfReportService _service =
        new(NullLogger<PdfReportService>.Instance, new FakeHorloge());

    public PdfReportServiceTests()
    {
        _repertoire = Path.Combine(Path.GetTempPath(), $"FrameworkTests-Pdf-{Guid.NewGuid():N}");
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
    public void Generer_RapportComplet_FichierOuvreEnPDF()
    {
        var chemin = Path.Combine(_repertoire, "rapport.pdf");

        var rapport = new RapportModele
        {
            Titre = "Facturation du mois",
            SousTitre = "Juin 2026",
            EnTeteInformations = ["Client : Salon Coiffure AM", "Poste : CAISSE-1"],
            Sections =
            [
                new SectionRapport
                {
                    Titre = "Prestations",
                    Colonnes = ["Libellé", "Quantité", "Montant"],
                    ColonnesDroites = [1, 2],
                    Lignes =
                    [
                        ["Coupe", "12", "180.00"],
                        ["Brosse", "4", "40.00"],
                    ],
                },
            ],
            Mentions = ["Merci de votre confiance."],
        };

        _service.Generer(rapport, chemin, "SalonDemo");

        var octets = File.ReadAllBytes(chemin);

        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(octets, 0, 4));
        Assert.True(octets.Length > 1000, "Un PDF de rapport ne doit jamais être un fichier vide.");
    }

    [Fact]
    public void Generer_RapportSansSection_PasDeCrash()
    {
        var chemin = Path.Combine(_repertoire, "vide.pdf");

        _service.Generer(new RapportModele { Titre = "Attestation" }, chemin, "FrameworkDemo");

        Assert.True(File.Exists(chemin));
    }
}
