using System.Text;
using Framework.Common.Licensing;
using Xunit;

namespace Framework.Tests;

/// <summary>Codec base32 Crockford : aller-retour octets, tolérance de saisie humaine.</summary>
public class LicenseCodecTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(12)]
    [InlineData(333)]
    public void Base32_AllerRetour_Fidèle(int taille)
    {
        var donnees = new byte[taille];
        Random.Shared.NextBytes(donnees);

        var encode = LicenseCodec.ToBase32(donnees);

        if (taille == 0)
        {
            Assert.Empty(encode);
            return;
        }

        var decode = LicenseCodec.FromBase32(encode);

        Assert.Equal(donnees, decode);
    }

    [Fact]
    public void Base32_Alphabet_SansILOGIN()
    {
        var encode = LicenseCodec.ToBase32(Enumerable.Range(0, 256).Select(i => (byte)i).ToArray());

        Assert.DoesNotContain('I', encode);
        Assert.DoesNotContain('L', encode);
        Assert.DoesNotContain('O', encode);
        Assert.DoesNotContain('U', encode);
    }

    [Fact]
    public void FormerPaquets_CinqParCinq()
    {
        var groupes = LicenseCodec.FormerPaquets("ABCDEFGHIJKLMNOPQRST").Split('-');

        Assert.Equal(4, groupes.Length);
        Assert.All(groupes, g => Assert.Equal(5, g.Length));
    }

    [Fact]
    public void Normaliser_TiretsEspacesEtRetours_Sortis()
    {
        var net = LicenseCodec.Normaliser(" abcde-fghijk \nlmno");

        Assert.Equal("abcdefghijklmno", net);
    }

    [Fact]
    public void FromBase32_CaractereHorsAlphabet_LeveFormatException()
    {
        Assert.Throws<FormatException>(() => LicenseCodec.FromBase32("!!!"));
    }
}
