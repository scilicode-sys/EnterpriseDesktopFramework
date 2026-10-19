using Framework.Common.Licensing;
using Xunit;

namespace Framework.Tests;

/// <summary>
/// Chaîne d'activation hors-ligne : le code émis par le distributeur (seul
/// détenteur de la clé) n'est valable QUE pour ce produit et ce matériel.
/// </summary>
public class OfflineLicenseEngineTests
{
    private const string Cle = "cle-secrete-de-test";
    private const string Produit = "AutoEcoleDemo";
    private const string Hwid = "1234ABCD-5678EFGH-9ABCDEF0-13579BDF";
    private static readonly DateOnly Expiration = new(2027, 6, 30);

    [Fact]
    public void Emettre_PuisValider_RendLExpiration()
    {
        var code = OfflineLicenseEngine.Emettre(Cle, Produit, Hwid, Expiration);

        Assert.Equal(4, code.Split('-').Length);
        Assert.True(OfflineLicenseEngine.Valider(Cle, Produit, Hwid, code, out var relue));
        Assert.Equal(Expiration, relue);
    }

    [Fact]
    public void Valider_MauvaiseCle_Echoue()
    {
        var code = OfflineLicenseEngine.Emettre(Cle, Produit, Hwid, Expiration);

        Assert.False(OfflineLicenseEngine.Valider("autre-cle", Produit, Hwid, code, out _));
    }

    [Fact]
    public void Valider_AutreMachine_Echoue()
    {
        var code = OfflineLicenseEngine.Emettre(Cle, Produit, Hwid, Expiration);

        Assert.False(OfflineLicenseEngine.Valider(
            Cle, Produit, "AAAA0000-BBBB1111-CCCC2222-DDDD3333", code, out _));
    }

    [Fact]
    public void Valider_AutreProduit_Echoue()
    {
        var code = OfflineLicenseEngine.Emettre(Cle, Produit, Hwid, Expiration);

        Assert.False(OfflineLicenseEngine.Valider(Cle, "SalonCoiffure", Hwid, code, out _));
    }

    [Fact]
    public void Valider_DateModifiéeDansLeCode_Echoue()
    {
        var code = OfflineLicenseEngine.Emettre(Cle, Produit, Hwid, Expiration);
        var nu = LicenseCodec.Normaliser(code);

        // Un caractère de plus : le blob change de forme et doit être refusé.
        Assert.False(OfflineLicenseEngine.Valider(Cle, Produit, Hwid, nu + "A", out _));
    }

    [Fact]
    public void Valider_SaisieTolérante_GroupementsEtCasse()
    {
        var code = OfflineLicenseEngine.Emettre(Cle, Produit, Hwid, Expiration);
        var recolie = code.ToLowerInvariant().Replace("-", " - ");

        Assert.True(OfflineLicenseEngine.Valider(Cle, Produit, Hwid, recolie, out _));
    }

    [Fact]
    public void Valider_EntréesVides_FauxSansException()
    {
        Assert.False(OfflineLicenseEngine.Valider("", Produit, Hwid, "AAAAA", out _));
        Assert.False(OfflineLicenseEngine.Valider(Cle, Produit, Hwid, "", out _));
    }

    [Fact]
    public void Emettre_CléVide_Leve()
    {
        Assert.Throws<ArgumentException>(() => OfflineLicenseEngine.Emettre("", Produit, Hwid, Expiration));
    }
}
