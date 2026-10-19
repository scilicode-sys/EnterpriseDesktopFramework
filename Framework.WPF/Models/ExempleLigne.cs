namespace Framework.WPF.Models;

/// <summary>
/// Ligne affichée par l'écran CRUD de référence. Volontairement distincte de
/// l'entité EF : la vue ne doit jamais tenir un objet suivi par un contexte,
/// sous peine de « The context is disposed » à l'affichage d'un champ différé.
/// </summary>
public sealed record ExempleLigne(
    Guid Id,
    string Libelle,
    string Categorie,
    decimal Montant,
    string? Note,
    DateTime? ModifieLe)
{
    /// <summary>Colonne montant telle que l'œil la veut : française, deux décimales.</summary>
    public string MontantAffiche => Montant.ToString("0.00");

    public string DerniereModification => ModifieLe?.ToString("dd/MM/yyyy HH:mm") ?? "—";
}
