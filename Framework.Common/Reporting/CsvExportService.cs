using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace Framework.Common.Reporting;

/// <summary>Réglages d'écriture CSV — séparateur point-virgule et BOM par défaut, pour Excel français.</summary>
public sealed record CsvOptions
{
    public char Separateur { get; init; } = ';';

    /// <summary>BOM UTF-8 en tête d' fichier : Excel ouvre les accents correctement.</summary>
    public bool BomUtf8 { get; init; } = true;

    public string FormatDate { get; init; } = "dd/MM/yyyy HH:mm";

    public string FormatDateCourt { get; init; } = "dd/MM/yyyy";
}

/// <summary>
/// Export CSV universel : n'importe quelle suite d'objets (entités, projections,
/// dictionnaires ordonnés) devient un fichier RFC 4180 lisible dans Excel.
/// Échappement par doubles quotes, formats de dates et booléens français
/// constants, culture invariante pour les nombres — testable sans UI.
/// </summary>
public static class CsvExportService
{
    /// <summary>Export par réflexion sur les propriétés publiques de T (ordre de déclaration).</summary>
    public static void Exporter<T>(IEnumerable<T> lignes, string cheminFichier, CsvOptions? options = null)
    {
        options ??= new CsvOptions();

        var proprietes = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var texte = new StringBuilder();
        texte.AppendLine(Ligne([.. proprietes.Select(p => p.Name)], options));

        foreach (var ligne in lignes)
        {
            texte.AppendLine(Ligne([.. proprietes.Select(p => Formater(p.GetValue(ligne), options))], options));
        }

        Ecrire(cheminFichier, texte.ToString(), options);
    }

    /// <summary>Export à colonnes choisies par l'écran (titres + cellules déjà en texte).</summary>
    public static void Exporter(IReadOnlyList<string> colonnes, IEnumerable<IReadOnlyList<string>> lignes, string cheminFichier, CsvOptions? options = null)
    {
        options ??= new CsvOptions();

        var texte = new StringBuilder();
        texte.AppendLine(Ligne(colonnes, options));

        foreach (var ligne in lignes)
        {
            texte.AppendLine(Ligne(ligne, options));
        }

        Ecrire(cheminFichier, texte.ToString(), options);
    }

    /// <summary>Construit une ligne CSV échappée (méthode exposée pour tests et cas sur mesure).</summary>
    public static string Ligne(IReadOnlyList<string?> cellules, CsvOptions options)
    {
        return string.Join(options.Separateur, cellules.Select(c => Echapper(c, options.Separateur)));
    }

    /// <summary>Un champ est entre guillemets s'il contient séparateur, quote ou saut de ligne ; les quotes se doublent.</summary>
    public static string Echapper(string? champ, char separateur)
    {
        if (string.IsNullOrEmpty(champ))
        {
            return string.Empty;
        }

        var aBesoinGuillemets = champ.Contains('"') || champ.Contains('\n') || champ.Contains('\r')
            || champ.Contains(separateur)
            || champ.StartsWith(' ') || champ.EndsWith(' ');

        if (aBesoinGuillemets)
        {
            return "\"" + champ.Replace("\"", "\"\"") + "\"";
        }

        return champ;
    }

    /// <summary>Formate une valeur .NET en texte français stable (sans dépendre de la culture du poste).</summary>
    public static string Formater(object? valeur, CsvOptions options)
    {
        return valeur switch
        {
            null => string.Empty,
            bool vrai => vrai ? "Oui" : "Non",
            DateTime date => date.ToString(options.FormatDate, CultureInfo.InvariantCulture),
            DateTimeOffset date => date.DateTime.ToString(options.FormatDate, CultureInfo.InvariantCulture),
            DateOnly jour => jour.ToString(options.FormatDateCourt, CultureInfo.InvariantCulture),
            TimeOnly heure => heure.ToString("HH:mm", CultureInfo.InvariantCulture),
            float nombre => nombre.ToString(CultureInfo.InvariantCulture),
            double nombre => nombre.ToString(CultureInfo.InvariantCulture),
            decimal nombre => nombre.ToString(CultureInfo.InvariantCulture),
            IFormattable formatable => formatable.ToString(null, CultureInfo.InvariantCulture),
            _ => valeur.ToString() ?? string.Empty,
        };
    }

    private static void Ecrire(string cheminFichier, string contenu, CsvOptions options)
    {
        var encoding = new UTF8Encoding(options.BomUtf8);
        var temporaire = cheminFichier + ".tmp";

        File.WriteAllText(temporaire, contenu, encoding);
        File.Move(temporaire, cheminFichier, overwrite: true);
    }
}
