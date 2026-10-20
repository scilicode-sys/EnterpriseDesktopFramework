namespace Framework.Core.Rbac;

/// <summary>
/// Matrice de départ du socle : trois rôles, une échelle, aucune règle métier.
/// Un produit dérivé la remplace (ou s'en inspire) à la composition root —
/// c'est là, et seulement là, que sa politique de droits s'écrit.
/// </summary>
public static class MatriceParDefaut
{
    public const string Administrateur = "Administrateur";
    public const string Utilisateur = "Utilisateur";
    public const string Lecteur = "Lecteur";

    public static MatriceRoles Creer() => new(new[]
    {
        new RoleDefini(Administrateur, "Administrateur", new[] { Droits.Tout })
        {
            Description = "Tous les écrans, toutes les écritures, y compris la configuration du poste.",
        },

        new RoleDefini(Utilisateur, "Utilisateur", new[]
        {
            Droits.TableauDeBordConsulter,
            Droits.ExemplesConsulter,
            Droits.ExemplesModifier,
            Droits.ExemplesSupprimer,
            Droits.ExemplesExporter,
            Droits.RapportsConsulter,
            Droits.RapportsGenerer,
            Droits.ComptesConsulter,
            Droits.ParametresConsulter,
        }, new[] { Lecteur })
        {
            Description = "Travaille sur les données du quotidien : il consulte les comptes et les réglages, il ne les gère pas.",
        },

        new RoleDefini(Lecteur, "Lecteur", new[]
        {
            Droits.TableauDeBordConsulter,
            Droits.ExemplesConsulter,
            Droits.RapportsConsulter,
            Droits.DroitsConsulter,
        })
        {
            Description = "Consulte sans écrire : poste d'accueil, atelier, borne.",
        },
    });
}
