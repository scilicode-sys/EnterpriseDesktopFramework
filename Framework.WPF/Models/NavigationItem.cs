namespace Framework.WPF.Models;

/// <summary>Entrée de la barre latérale : libellé, glyphe Segoe MDL2, type du ViewModel cible.</summary>
public sealed record NavigationItem(string Libelle, string Glyph, Type Cible);
