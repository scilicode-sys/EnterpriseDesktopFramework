using Framework.Core.Rbac;
using Framework.WPF.ViewModels;
using Xunit;

namespace Framework.Tests;

/// <summary>
/// Écran de contrôle du support : ce que la matrice déclare, ce que la session
/// tient, et les deux ratés qu'il doit crier — une session sans droit d'entrée,
/// un rôle déclaré qui ne mène à aucun écran.
/// </summary>
public class DroitsViewModelTests
{
    private static DroitsViewModel creer(string? role, IMatriceRoles? matrice = null)
    {
        var session = new FakeSession(role);
        var grille = matrice ?? MatriceParDefaut.Creer();
        return new DroitsViewModel(session, grille, new AutorisationService(session, grille));
    }

    [Fact]
    public void Charger_HorsSession_RoleVideEtRefusAffiche()
    {
        var ecran = creer(null);
        ecran.Charger();

        Assert.Empty(ecran.Roles);
        Assert.Equal("(hors session)", ecran.SessionCourante);
        Assert.Contains("Accès refusé", ecran.Avertissement, StringComparison.Ordinal);
        Assert.True(ecran.EstAvertissementVisible);
    }

    [Fact]
    public void Charger_RoleNonDeclare_LeRefusEstLeMemeEtRienNEstFuite()
    {
        var ecran = creer("Comptable");
        ecran.Charger();

        Assert.Empty(ecran.Roles);
        Assert.Contains("droits.consulter", ecran.Avertissement, StringComparison.Ordinal);
    }

    [Fact]
    public void Charger_Lecteur_DerouleLaMatriceEtMarqueLaSession()
    {
        var ecran = creer(MatriceParDefaut.Lecteur);
        ecran.Charger();

        Assert.Equal(new[] { "Administrateur", "Lecteur", "Utilisateur" }, ecran.Roles.Select(r => r.Cle).ToArray());
        Assert.Equal("Agent Test — rôle « Lecteur »", ecran.SessionCourante);
        Assert.False(ecran.EstAvertissementVisible);

        var courant = Assert.Single(ecran.Roles.Where(r => r.EstRoleCourant));
        Assert.Equal(MatriceParDefaut.Lecteur, courant.Cle);
        Assert.Equal("aucun", courant.HeriteDe);

        Assert.Equal("Lecteur", ecran.Roles.Single(r => r.Cle == "Utilisateur").HeriteDe);
    }

    [Fact]
    public void Charger_Administrateur_LaSauvageonneEstDiteSansEtreListee()
    {
        var ecran = creer(MatriceParDefaut.Administrateur);
        ecran.Charger();

        var admin = ecran.Roles.Single(r => r.Cle == MatriceParDefaut.Administrateur);

        Assert.Equal(new[] { Droits.Tout }, admin.Permissions);
        Assert.Equal("toutes permissions", admin.Compte);
        Assert.Contains("« * »", admin.Resume, StringComparison.Ordinal);
    }

    [Fact]
    public void Charger_Utilisateur_HeriteCompteLesPermissionsDuDessous()
    {
        var ecran = creer(MatriceParDefaut.Utilisateur);
        ecran.Charger();

        var utilisateur = ecran.Roles.Single(r => r.Cle == MatriceParDefaut.Utilisateur);

        // Les 9 permissions propres + droits.consulter venu de Lecteur.
        Assert.Equal(10, utilisateur.Permissions.Count);
        Assert.Contains(Droits.DroitsConsulter, utilisateur.Permissions);
        Assert.Equal("10 permissions", utilisateur.Compte);
        Assert.Contains(" · ", utilisateur.Resume, StringComparison.Ordinal);
    }

    [Fact]
    public void Charger_RoleDeclareSansDroit_LeSignaleCommeImpasse()
    {
        var matrice = new MatriceRoles(new[]
        {
            new RoleDefini(MatriceParDefaut.Administrateur, "Administrateur", new[] { Droits.Tout }),
            new RoleDefini("Stagiaire", "Stagiaire", RoleDefini.Aucune),
            new RoleDefini("Assistant", "Assistant", RoleDefini.Aucune),
        });

        var ecran = creer(MatriceParDefaut.Administrateur, matrice);
        ecran.Charger();

        Assert.Contains("« Stagiaire », « Assistant »", ecran.Avertissement, StringComparison.Ordinal);
        Assert.Contains("ne donnent accès", ecran.Avertissement, StringComparison.Ordinal);
        Assert.True(ecran.EstAvertissementVisible);

        var stagiaire = ecran.Roles.Single(r => r.Cle == "Stagiaire");
        Assert.Equal("aucune permission", stagiaire.Compte);
        Assert.Equal("—", stagiaire.Resume);
    }

    [Fact]
    public void Recharger_ApresUnChangementDeSession_ReposeLaSessionCourante()
    {
        var session = new FakeSession(MatriceParDefaut.Lecteur);
        var grille = MatriceParDefaut.Creer();
        var ecran = new DroitsViewModel(session, grille, new AutorisationService(session, grille));

        ecran.Charger();
        Assert.DoesNotContain(ecran.Roles, r => r.EstRoleCourant && r.Cle == MatriceParDefaut.Administrateur);

        session.SeConnecter(MatriceParDefaut.Administrateur);
        ecran.RechargerCommand.Execute(null);

        Assert.Equal("Agent Test — rôle « Administrateur »", ecran.SessionCourante);
        Assert.True(ecran.Roles.Single(r => r.Cle == MatriceParDefaut.Administrateur).EstRoleCourant);
    }
}
