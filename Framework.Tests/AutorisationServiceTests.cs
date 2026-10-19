using Framework.Core.Auth;
using Framework.Core.Rbac;
using Xunit;

namespace Framework.Tests;

/// <summary>
/// Le service d'autorisation ne fait que brancher la session sur la matrice —
/// et c'est précisément ce branchement qu'il faut verrouiller : hors session,
/// tout est refusé, même la sauvageonne ; et <c>Exiger</c> lève au lieu de
/// laisser l'écriture passer silencieusement.
/// </summary>
public class AutorisationServiceTests
{
    private static (FakeSession session, AutorisationService autorisation) deux(string role)
    {
        var session = new FakeSession(role);
        return (session, new AutorisationService(session, MatriceParDefaut.Creer()));
    }

    [Fact]
    public void HorsSession_RienNestAccorde()
    {
        var autorisation = new AutorisationService(new FakeSession(), MatriceParDefaut.Creer());

        Assert.False(autorisation.EstConnecte);
        Assert.Null(autorisation.RoleCourant);
        Assert.Empty(autorisation.PermissionsCourantes);
        Assert.False(autorisation.Peut(Droits.Tout));
        Assert.False(autorisation.Peut(Droits.TableauDeBordConsulter));
    }

    [Fact]
    public void Administrateur_PeutToutYComprisUnePermissionDuProduit()
    {
        var (_, autorisation) = deux(MatriceParDefaut.Administrateur);

        Assert.True(autorisation.EstConnecte);
        Assert.Equal(MatriceParDefaut.Administrateur, autorisation.RoleCourant);
        Assert.True(autorisation.Peut(Droits.ComptesGerer));
        Assert.True(autorisation.Peut(Droits.Cle("stock", Droits.VerbeExporter)));
        Assert.Equal(new[] { Droits.Tout }, autorisation.PermissionsCourantes);
    }

    [Fact]
    public void Lecteur_ConsulteSansEcrire()
    {
        var (_, autorisation) = deux(MatriceParDefaut.Lecteur);

        Assert.True(autorisation.Peut(Droits.ExemplesConsulter));
        Assert.True(autorisation.Peut(Droits.DroitsConsulter));
        Assert.False(autorisation.Peut(Droits.ExemplesModifier));
        Assert.False(autorisation.Peut(Droits.ExemplesSupprimer));
    }

    [Fact]
    public void RoleNonDeclare_LaSessionExisteMaisNePeutRien()
    {
        var (_, autorisation) = deux("Comptable");

        Assert.True(autorisation.EstConnecte);
        Assert.Equal("Comptable", autorisation.RoleCourant);
        Assert.Empty(autorisation.PermissionsCourantes);
        Assert.False(autorisation.Peut(Droits.TableauDeBordConsulter));
    }

    [Fact]
    public void Exiger_DroitAbsent_LeveAvecLaPermissionEtLeRole()
    {
        var (_, autorisation) = deux(MatriceParDefaut.Lecteur);

        var erreur = Assert.Throws<AutorisationRefuseeException>(() => autorisation.Exiger(Droits.ExemplesModifier));

        Assert.Equal(Droits.ExemplesModifier, erreur.Permission);
        Assert.Equal(MatriceParDefaut.Lecteur, erreur.RoleCle);
        Assert.Contains("exemples.modifier", erreur.Message, StringComparison.Ordinal);
        Assert.Contains("Lecteur", erreur.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Exiger_DroitPresent_NLevePas()
    {
        var (_, autorisation) = deux(MatriceParDefaut.Utilisateur);

        autorisation.Exiger(Droits.ExemplesSupprimer);
        autorisation.Exiger(Droits.DroitsConsulter);
    }

    [Fact]
    public void Exiger_HorsSession_LeveSansCiterDeRole()
    {
        var autorisation = new AutorisationService(new FakeSession(), MatriceParDefaut.Creer());

        var erreur = Assert.Throws<AutorisationRefuseeException>(() => autorisation.Exiger(Droits.ExemplesConsulter));

        Assert.Null(erreur.RoleCle);
        Assert.DoesNotContain("« »", erreur.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangementDeSession_LesDroitsSuiventSansRecycl()
    {
        // Le service est lu à chaque demande : fermer la session retire les droits
        // aussitôt, sans redémarrage ni cache à invalider.
        var session = new FakeSession(MatriceParDefaut.Administrateur);
        var autorisation = new AutorisationService(session, MatriceParDefaut.Creer());

        Assert.True(autorisation.Peut(Droits.ComptesGerer));

        session.SeConnecter(MatriceParDefaut.Lecteur);
        Assert.False(autorisation.Peut(Droits.ComptesGerer));
        Assert.True(autorisation.Peut(Droits.ExemplesConsulter));

        session.FermerSession();
        Assert.False(autorisation.Peut(Droits.ExemplesConsulter));
    }

    [Fact]
    public void MatriceDuProduit_LaSienneRemplaceCelleDuSocle()
    {
        var matrice = new MatriceRoles(new[]
        {
            new RoleDefini("Superviseur", "Superviseur", new[] { Droits.Cle("postes", Droits.VerbeGerer) }),
        });
        var autorisation = new AutorisationService(new FakeSession("Superviseur"), matrice);

        Assert.True(autorisation.Peut("postes.gerer"));
        Assert.False(autorisation.Peut(Droits.ExemplesConsulter));
    }

    [Fact]
    public void ServiceDeSessionReel_MemeContratQuUneSessionDeTest()
    {
        // AuthService écrit bien le rôle du compte dans CurrentUser : c'est le seul
        // contrat entre la base, la connexion et la matrice.
        IAuthService session = new FakeSession(MatriceParDefaut.Utilisateur);
        var autorisation = new AutorisationService(session, MatriceParDefaut.Creer());

        Assert.Equal("Utilisateur", autorisation.RoleCourant);
        Assert.Contains(Droits.ExemplesModifier, autorisation.PermissionsCourantes);
    }
}
