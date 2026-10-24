using G27Hub.Core;

namespace G27Hub.Core.Tests;

public class UpdatesTests
{
    const string Json = """{"tag_name":"v1.1.0","html_url":"https://github.com/Samosbourne/G27Hub/releases/tag/v1.1.0"}""";

    [Fact]
    public void Avisa_so_quando_a_versao_publicada_e_mais_nova()
    {
        var found = Updates.Parse(Json, new Version(1, 0, 0, 0)); // assembly: 1.0.0.0
        Assert.Equal(new Version(1, 1, 0), found!.Value.Version);
        Assert.EndsWith("/v1.1.0", found.Value.Url);

        Assert.Null(Updates.Parse(Json, new Version(1, 1, 0, 0)));
        Assert.Null(Updates.Parse(Json, new Version(1, 2, 0)));
        Assert.Null(Updates.Parse("""{"tag_name":"teste"}""", new Version(1, 0, 0)));
    }
}

public class ForceCurveTests
{
    [Fact]
    public void Tabela_de_reforco_sobe_o_comeco_e_mantem_os_100()
    {
        Assert.Null(FfbRegistration.ForceLut(0)); // linear: sem tabela
        var lut = FfbRegistration.ForceLut(50)!.Split(',').Select(int.Parse).ToArray();
        Assert.Equal(11, lut.Length);
        Assert.Equal(0, lut[0]);
        Assert.Equal(10000, lut[10]);
        Assert.True(lut[1] > 1000 && lut.Zip(lut.Skip(1)).All(p => p.First < p.Second));
    }
}
