namespace Framework.Common.Reporting;

/// <summary>
/// Modèle de rapport indépendant du métier : un titre, des sections de
/// tableaux, des mentions. Tout écran métier sait assembler cette forme —
/// le rendu (PDF) ne connaît aucune entité du projet.
/// </summary>
public sealed record RapportModele
{
    public required string Titre { get; init; }

    public string? SousTitre { get; init; }

    /// <summary>Lignes libres sous le titre : client, poste, période…</summary>
    public IReadOnlyList<string> EnTeteInformations { get; init; } = Array.Empty<string>();

    public IReadOnlyList<SectionRapport> Sections { get; init; } = Array.Empty<SectionRapport>();

    /// <summary>Bas de page : mentions légales, signature, total général.</summary>
    public IReadOnlyList<string> Mentions { get; init; } = Array.Empty<string>();
}

/// <summary>Une section = un titre optionnel + un tableau (colonnes puis lignes).</summary>
public sealed record SectionRapport
{
    public string? Titre { get; init; }

    public IReadOnlyList<string> Colonnes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<IReadOnlyList<string>> Lignes { get; init; } = Array.Empty<IReadOnlyList<string>>();

    /// <summary>Colonnes alignées à droite (montants, quantités) — index de colonne.</summary>
    public IReadOnlyList<int> ColonnesDroites { get; init; } = Array.Empty<int>();
}
