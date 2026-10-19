namespace Framework.WPF.Models;

/// <summary>
/// Entrée de la barre latérale : libellé, glyphe Segoe MDL2, type du ViewModel
/// cible et permission requise pour l'afficher. Une permission nulle laisse
/// l'entrée visible par toute session — c'est le cas du tableau de bord.
/// </summary>
public sealed record NavigationItem(
    string Libelle,
    string Glyph,
    Type Cible,
    string? PermissionRequise = null);
