namespace Framework.Data.Entities;

/// <summary>
/// Entité **de référence** du framework : elle n'a aucun sens métier, elle
/// sert à montrer — et à copier — ce qu'une table du produit doit avoir pour
/// être portée par le socle : une clé GUID, un champ texte, une catégorie
/// libre, un montant, et l'audit + suppression logique hérités d'<see cref="EntityBase"/>.
/// </summary>
/// <remarks>
/// Un produit dérivé supprime cette classe et sa migration dès qu'il pose sa
/// première vraie table ; tant qu'il n'y en a pas, elle démontre le CRUD, la
/// synchro inter-postes et l'export CSV/PDF sur une base réelle.
/// </remarks>
public class ExempleEntity : EntityBase
{
    public string Libelle { get; set; } = string.Empty;

    /// <summary>Chaîne libre : c'est le produit qui fixe sa taxonomy, pas le socle.</summary>
    public string Categorie { get; set; } = string.Empty;

    public decimal Montant { get; set; }

    public string? Note { get; set; }
}
