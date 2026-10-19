using System.Globalization;
using System.IO;
using Framework.Core.Time;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Framework.Common.Reporting;

/// <summary>
/// Générateur de rapports PDF (QuestPDF) à partir du modèle neutre
/// <see cref="RapportModele"/> : en-tête produit + date, sections en tableaux
/// bordés, pied de page numéroté. Aucun type métier ne transite ici.
/// </summary>
public sealed class PdfReportService
{
    static PdfReportService()
    {
        // QuestPDF (MIT) exige la déclaration explicite du régime d'usage.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private readonly ILogger<PdfReportService> _logger;
    private readonly IHorloge _horloge;

    public PdfReportService(ILogger<PdfReportService> logger, IHorloge horloge)
    {
        _logger = logger;
        _horloge = horloge;
    }

    /// <summary>Rédige le rapport et l'écrit dans un fichier (atomique via temporaire).</summary>
    public void Generer(RapportModele rapport, string cheminFichier, string nomProduit)
    {
        var temporaire = cheminFichier + ".tmp";

        using (var flux = File.Create(temporaire))
        {
            Document.Create(document =>
            {
                document.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Element(e => RedigerEnTete(e, rapport, nomProduit));                    page.Content().PaddingVertical(8).Column(col =>
                    {
                        foreach (var section in rapport.Sections)
                        {
                            col.Item().Element(e => RedigerSection(e, section));
                        }

                        if (rapport.Mentions.Count > 0)
                        {
                            col.Item().PaddingTop(16).Column(mention =>
                            {
                                foreach (var ligne in rapport.Mentions)
                                {
                                    mention.Item().Text(ligne).FontSize(9).FontColor(Colors.Grey.Darken1);
                                }
                            });
                        }
                    });
                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Grey.Darken1));
                        t.Span("Page ");
                        t.CurrentPageNumber();
                        t.Span(" / ");
                        t.TotalPages();
                    });
                });
            }).GeneratePdf(flux);
        }

        File.Move(temporaire, cheminFichier, overwrite: true);
        _logger.LogInformation("Rapport PDF « {Titre} » écrit : {Chemin}.", rapport.Titre, cheminFichier);
    }

    private void RedigerEnTete(IContainer conteneur, RapportModele rapport, string nomProduit)
    {
        conteneur.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(rapport.Titre).SemiBold().FontSize(16);
                    if (!string.IsNullOrEmpty(rapport.SousTitre))
                    {
                        c.Item().Text(rapport.SousTitre).FontSize(11).FontColor(Colors.Grey.Darken1);
                    }
                });

                row.ConstantItem(170).AlignTop().AlignRight().Column(c =>
                {
                    c.Item().Text(nomProduit).SemiBold().FontSize(10);
                    c.Item().Text(_horloge.Maintenant.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture))
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            });

            foreach (var information in rapport.EnTeteInformations)
            {
                col.Item().PaddingTop(4).Text(information).FontSize(9).FontColor(Colors.Grey.Darken1);
            }

            col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
        });
    }

    private static void RedigerSection(IContainer conteneur, SectionRapport section)
    {
        conteneur.Column(col =>
        {
            col.Spacing(4);

            if (!string.IsNullOrEmpty(section.Titre))
            {
                col.Item().PaddingTop(10).Text(section.Titre).SemiBold().FontSize(12);
            }

            if (section.Colonnes.Count == 0)
            {
                return;
            }

            col.Item().Border(1).BorderColor(Colors.Grey.Lighten1).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    for (var i = 0; i < section.Colonnes.Count; i++)
                    {
                        columns.RelativeColumn();
                    }
                });

                table.Header(header =>
                {
                    foreach (var colonne in section.Colonnes)
                    {
                        header.Cell()
                            .Background(Colors.Grey.Lighten3)
                            .Padding(4)
                            .Text(colonne).SemiBold().FontSize(9);
                    }
                });

                foreach (var ligne in section.Lignes)
                {
                    for (var i = 0; i < section.Colonnes.Count; i++)
                    {
                        var valeur = i < ligne.Count ? ligne[i] : string.Empty;
                        var cellule = table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4);

                        if (section.ColonnesDroites.Contains(i))
                        {
                            cellule.AlignRight().AlignCenter().Text(valeur).FontSize(9);
                        }
                        else
                        {
                            cellule.AlignLeft().AlignCenter().Text(valeur).FontSize(9);
                        }
                    }
                }
            });
        });
    }
}
