using Framework.Core.Security;
using Xunit;

namespace Framework.Tests;

/// <summary>Hachage PBKDF2 : format auto-descriptif, comparaison temps constant.</summary>
public class PasswordHasherTests
{
    [Fact]
    public void Hash_PuisVerify_MotDePasseCorrect_Valide()
    {
        var hash = PasswordHasher.Hash("MotDePasse!2026");

        Assert.True(PasswordHasher.Verify("MotDePasse!2026", hash));
    }

    [Fact]
    public void Verify_MotDePasseFaux_Echoue()
    {
        var hash = PasswordHasher.Hash("bon");

        Assert.False(PasswordHasher.Verify("mauvais", hash));
    }

    [Fact]
    public void Hash_DeuxAppels_SelsDifferents()
    {
        var premier = PasswordHasher.Hash("identique");
        var second = PasswordHasher.Hash("identique");

        Assert.NotEqual(premier, second);
        Assert.True(PasswordHasher.Verify("identique", premier));
        Assert.True(PasswordHasher.Verify("identique", second));
    }

    [Fact]
    public void Hash_FormatDescriptif_Pbkdf2Sha256()
    {
        var hash = PasswordHasher.Hash("x");
        var parties = hash.Split('$');

        Assert.Equal(5, parties.Length);
        Assert.Equal("pbkdf2", parties[0]);
        Assert.Equal("sha256", parties[1]);
        Assert.Equal(150_000, int.Parse(parties[2]));
    }

    [Theory]
    [InlineData(null, "peu-importe")]
    [InlineData("peu-importe", null)]
    [InlineData("", "")]
    [InlineData("x", "pas-un-format-valide")]
    [InlineData("x", "pbkdf2$md5$150000$c2FsdA==$a2V5")]
    [InlineData("x", "pbkdf2$sha256$99$c2FsdA==$a2V5")]
    public void Verify_EntreesMalFormees_RefusSansException(string? password, string? hash)
    {
        Assert.False(PasswordHasher.Verify(password, hash));
    }

    [Fact]
    public void Hash_MotDePasseVide_Leve()
    {
        Assert.Throws<ArgumentException>(() => PasswordHasher.Hash(""));
    }

    [Fact]
    public void Verify_EmpreinteAlteree_Echoue()
    {
        var hash = PasswordHasher.Hash("original");
        var parties = hash.Split('$');

        // On échange sel et empreinte : la relecture doit refuser, pas craquer.
        var altere = string.Join('$', parties[0], parties[1], parties[2], parties[4], parties[3]);

        Assert.False(PasswordHasher.Verify("original", altere));
    }
}
