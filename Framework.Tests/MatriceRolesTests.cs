using Framework.Core.Rbac;
using Xunit;

namespace Framework.Tests;

/// <summary>
/// La matrice est le juge : héritage aplati à la construction, sauvageonne
/// ascendante, et surtout refus silencieux devant un rôle qu'elle ne connaît
/// pas. Ces règles sont du code, pas de la base — elles se testent donc sans
/// DbContext.
/// </summary>
public class MatriceRolesTests
{
    private static MatriceRoles troisNiveaux() => new(new[]
    {
        new RoleDefini("Administrateur", "Administrateur", new[] { Droits.Tout }, new[] { "Editeur" }),
        new RoleDefini("Editeur", "Editeur", new[] { Droits.ExemplesModifier }, new[] { "Lecteur" }),
        new RoleDefini("Lecteur", "Lecteur", new[] { Droits.ExemplesConsulter }),
    });

    [Fact]
    public void Herite_AplatitLEchelleSansRecopier()
    {
        var matrice = troisNiveaux();

        Assert.True(matrice.EstAccordee("Editeur", Droits.ExemplesModifier));
        Assert.True(matrice.EstAccordee("Editeur", Droits.ExemplesConsulter));
        Assert.False(matrice.EstAccordee("Lecteur", Droits.ExemplesModifier));
    }

    [Fact]
    public void Tout_RemonteLEchelleEtDonneToutAuxRolesDuDessus()
    {
        // La sauvageonne est posée sur le rôle du haut : rien à déclarer en dessous.
        var matrice = troisNiveaux();

        Assert.True(matrice.EstAccordee("Administrateur", Droits.ComptesGerer));
        Assert.True(matrice.EstAccordee("Administrateur", "module.inconnu.verbe"));
        Assert.True(matrice.EstAccordee("Administrateur", Droits.ExemplesConsulter));
        Assert.False(matrice.EstAccordee("Lecteur", Droits.ComptesGerer));
    }

    [Fact]
    public void RolesConnus_EtRoleInconnuNeDonneRien()
    {
        var matrice = troisNiveaux();

        Assert.True(matrice.Connait("Lecteur"));
        Assert.False(matrice.Connait("Comptable"));
        Assert.False(matrice.Connait(null));
        Assert.False(matrice.Connait("  "));

        Assert.False(matrice.EstAccordee("Comptable", Droits.TableauDeBordConsulter));
        Assert.Empty(matrice.PermissionsDe("Comptable"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CleDeRoleVide_AucunDroitJamais(string? cle)
    {
        var matrice = troisNiveaux();

        Assert.False(matrice.EstAccordee(cle, Droits.Tout));
        Assert.Empty(matrice.PermissionsDe(cle));
    }

    [Fact]
    public void Comparaisons_IgnoresLaCasseEtTriEntLesPermissions()
    {
        var matrice = troisNiveaux();

        Assert.True(matrice.Connait("LECTEUR"));
        Assert.True(matrice.EstAccordee("lecteur", "EXEMPLES.CONSULTER"));

        Assert.Equal(
            new[] { Droits.ExemplesConsulter, Droits.ExemplesModifier },
            matrice.PermissionsDe("Editeur"));
    }

    [Fact]
    public void PermissionsDe_AplatitSansDoublon()
    {
        var matrice = new MatriceRoles(new[]
        {
            new RoleDefini("Haut", "Haut", new[] { Droits.ExemplesConsulter, "comptes.consulter", Droits.ExemplesConsulter }, new[] { "Bas" }),
            new RoleDefini("Bas", "Bas", new[] { Droits.ExemplesConsulter, "rapports.consulter" }),
        });

        Assert.Equal(
            new[] { "comptes.consulter", Droits.ExemplesConsulter, "rapports.consulter" },
            matrice.PermissionsDe("Haut"));
    }

    [Fact]
    public void CycleDHeritage_CoupePileSansExploser()
    {
        var matrice = new MatriceRoles(new[]
        {
            new RoleDefini("Alpha", "Alpha", new[] { Droits.ExemplesConsulter }, new[] { "Beta" }),
            new RoleDefini("Beta", "Beta", new[] { Droits.ExemplesModifier }, new[] { "Alpha" }),
        });

        Assert.True(matrice.EstAccordee("Alpha", Droits.ExemplesModifier));
        Assert.True(matrice.EstAccordee("Beta", Droits.ExemplesConsulter));
        Assert.Equal(2, matrice.PermissionsDe("Alpha").Count);
    }

    [Fact]
    public void CleDeRoleDoublonne_LeRefuseALaConstruction()
    {
        var construire = () => new MatriceRoles(new[]
        {
            new RoleDefini("Lecteur", "Lecteur", new[] { Droits.ExemplesConsulter }),
            new RoleDefini("lecteur", "Lecteur bis", new[] { Droits.RapportsConsulter }),
        });

        var erreur = Assert.Throws<InvalidOperationException>(construire);
        Assert.Contains("même clef", erreur.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HeriteDUnRoleInexistant_IgnoreSansInventerDeDroit()
    {
        var matrice = new MatriceRoles(new[]
        {
            new RoleDefini("Solo", "Solo", new[] { Droits.ExemplesConsulter }, new[] { "Fantôme" }),
        });

        Assert.Equal(new[] { Droits.ExemplesConsulter }, matrice.PermissionsDe("Solo"));
        Assert.False(matrice.Connait("Fantôme"));
    }

    [Fact]
    public void ParDefaut_TroisRolesEtAdministrateurQuiToutPermet()
    {
        var matrice = MatriceParDefaut.Creer();

        Assert.Equal(3, matrice.Roles.Count);
        Assert.Equal(new[] { "Administrateur", "Utilisateur", "Lecteur" }, matrice.Roles.Select(r => r.Cle).ToArray());

        Assert.Equal(new[] { Droits.Tout }, matrice.PermissionsDe(MatriceParDefaut.Administrateur));

        Assert.True(matrice.EstAccordee(MatriceParDefaut.Utilisateur, Droits.ExemplesSupprimer));
        // Utilisateur inherits Lecteur : il tient donc la consultation des droits.
        Assert.True(matrice.EstAccordee(MatriceParDefaut.Utilisateur, Droits.DroitsConsulter));
        Assert.False(matrice.EstAccordee(MatriceParDefaut.Utilisateur, Droits.ComptesGerer));

        Assert.True(matrice.EstAccordee(MatriceParDefaut.Lecteur, Droits.ExemplesConsulter));
        Assert.True(matrice.EstAccordee(MatriceParDefaut.Lecteur, Droits.DroitsConsulter));
        Assert.False(matrice.EstAccordee(MatriceParDefaut.Lecteur, Droits.ExemplesModifier));
        Assert.False(matrice.EstAccordee(MatriceParDefaut.Lecteur, Droits.ExemplesSupprimer));
    }

    [Fact]
    public void CompositionDeCle_ModuleProduitSansSaisieLibre()
    {
        var matrice = new MatriceRoles(new[]
        {
            new RoleDefini("Atelier", "Atelier", new[] { Droits.Cle("postes", Droits.VerbeGerer) }),
        });

        Assert.True(matrice.EstAccordee("Atelier", "postes.gerer"));
        Assert.Equal(new[] { "postes.gerer" }, matrice.PermissionsDe("Atelier"));
    }
}
