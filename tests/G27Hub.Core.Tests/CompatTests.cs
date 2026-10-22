using G27Hub.Core;

namespace G27Hub.Core.Tests;

public class CompatTests
{
    static readonly CompatDb Db = CompatDb.LoadEmbedded();

    [Fact]
    public void EmbeddedDbHasTheSection8GamesWithUniqueIdsAndExes()
    {
        Assert.True(Db.Games.Count >= 17);
        Assert.Equal(Db.Games.Count, Db.Games.Select(g => g.Id).Distinct().Count());
        var exes = Db.Games.SelectMany(g => g.Exes).Select(e => e.ToLowerInvariant()).ToList();
        Assert.Equal(exes.Count, exes.Distinct().Count()); // um .exe nunca aponta para dois jogos
        Assert.All(Db.Games, g => Assert.Contains(g.Status, new[] { "nativo", "directinput", "precisa-compat-total", "limitado", "desconhecido" }));
        Assert.All(Db.Games.Where(g => g.Confidence == "oficial"), g => Assert.StartsWith("https://", g.Source));
    }

    [Theory]
    [InlineData("ForzaHorizon5", "forza-horizon-5")]
    [InlineData("forzahorizon5.exe", "forza-horizon-5")]
    [InlineData("AC2-Win64-Shipping", "assetto-corsa-competizione")]
    [InlineData("BeamNG.drive.x64", "beamng-drive")] // ponto no nome do processo (bug real: não detectava)
    [InlineData("chrome", null)]
    public void FindsByProcessOrExeName(string name, string? id) => Assert.Equal(id, Db.Find(name)?.Id);

    [Fact]
    public void ForzaWithXboxControllerAndSteamWarns()
    {
        var fh5 = Db.Find("ForzaHorizon5")!;
        var advice = Preflight.Check(fh5, new PreflightContext(WheelConnected: true, NativeMode: true, XboxControllers: 1, SteamRunning: true, AppRange: 900));
        Assert.Contains(advice, a => a.Kind == "Ok" && a.Body.Contains("Compatível"));
        Assert.Contains(advice, a => a.Kind == "Warn" && a.Body.Contains("controle de Xbox"));
        Assert.Contains(advice, a => a.Body.Contains("Steam Input"));
        Assert.Contains(advice, a => a.Body.Contains("540°")); // rotação sugerida diferente do app
    }

    [Fact]
    public void NeedsCompatModeOnlyRecommends()
    {
        var game = new CompatEntry { Name = "Jogo X", Status = "precisa-compat-total", SuggestedRange = 900 };
        var advice = Preflight.Check(game, new PreflightContext(true, true, 0, false, 900));
        Assert.Single(advice);
        Assert.Equal("Accent", advice[0].Kind);
        Assert.Contains("Recomendado", advice[0].Body);
    }

    [Fact]
    public void NativeGameWithCompatOnRecommendsStandard()
    {
        var game = new CompatEntry { Name = "Forza", Status = "nativo", SuggestedRange = 900 };
        var on = Preflight.Check(game, new PreflightContext(true, true, 0, false, 900, CompatModeOn: true));
        Assert.Contains(on, a => a.Body.Contains("desligar o Compatibilidade Total"));
        var off = Preflight.Check(new CompatEntry { Name = "X", Status = "precisa-compat-total", SuggestedRange = 900 }, new PreflightContext(true, true, 0, false, 900, CompatModeOn: true));
        Assert.DoesNotContain(off, a => a.Body.Contains("Recomendado"));
    }

    [Fact]
    public void NotNativeAndBadVotesWarn()
    {
        var game = new CompatEntry { Name = "Y", Status = "nativo", SuggestedRange = 900 };
        var advice = Preflight.Check(game, new PreflightContext(true, NativeMode: false, 0, false, 900), new UserCompat.Result { Bad = 2, Good = 1 });
        Assert.Contains(advice, a => a.Body.Contains("modo nativo"));
        Assert.Contains(advice, a => a.Body.Contains("👎"));
    }

    [Fact]
    public void XInputOnlyHeuristic()
    {
        Assert.True(Preflight.LooksXInputOnly([@"C:\Windows\System32\XINPUT1_4.dll", "kernel32.dll"]));
        Assert.False(Preflight.LooksXInputOnly(["xinput1_3.dll", "DINPUT8.dll"]));
        Assert.False(Preflight.LooksXInputOnly(["kernel32.dll"]));
    }

    [Fact]
    public void ParsesSteamLibraryFolders()
    {
        const string vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t}\n}";
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], GameScanner.ParseLibraryFolders(vdf));
    }

    [Fact]
    public void UserCompatRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"g27hub-user-{Guid.NewGuid():N}.json");
        var u = UserCompat.Load(path);
        u.Vote("forza-horizon-5", good: true);
        u.For("nfs").Muted = true;
        u.Save();
        var back = UserCompat.Load(path);
        Assert.Equal(1, back.For("forza-horizon-5").Good);
        Assert.True(back.IsMuted("nfs"));
        Assert.False(back.IsMuted("forza-horizon-5"));
        File.Delete(path);
    }
}
