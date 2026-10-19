namespace Framework.Core.Rbac;

/// <summary>
/// Clés de permission du framework. Convention <c>module.verbe</c> en minuscules :
/// une permission n'est jamais comparée à une saisie libre, elle vient d'ici.
/// Un produit dérivé garde ces constantes-là et y ajoute les siennes — c'est le
/// seul endroit où une permission nouvelle a le droit d'apparaître.
/// </summary>
public static class Droits
{
    /// <summary>Sauvageonne : toutes les permissions. Réservée au rôle le plus haut.</summary>
    public const string Tout = "*";

    /// <summary>Verbes standard : un module s'autorise en les composant, pas en inventant un synonyme.</summary>
    public const string VerbeConsulter = "consulter";
    public const string VerbeModifier = "modifier";
    public const string VerbeSupprimer = "supprimer";
    public const string VerbeExporter = "exporter";
    public const string VerbeGerer = "gerer";

    public const string TableauDeBordConsulter = "tableaubord.consulter";
    public const string ExemplesConsulter = "exemples.consulter";
    public const string ExemplesModifier = "exemples.modifier";
    public const string ExemplesSupprimer = "exemples.supprimer";
    public const string ExemplesExporter = "exemples.exporter";
    public const string RapportsConsulter = "rapports.consulter";
    public const string RapportsGenerer = "rapports.generer";
    public const string ComptesConsulter = "comptes.consulter";
    public const string ComptesGerer = "comptes.gerer";
    public const string DroitsConsulter = "droits.consulter";
    public const string ParametresConsulter = "parametres.consulter";
    public const string ParametresGerer = "parametres.gerer";
    public const string LicenceGerer = "licence.gerer";

    /// <summary>Composition <c>module.verbe</c> pour un module propre au produit.</summary>
    public static string Cle(string module, string verbe) => $"{module}.{verbe}";
}
