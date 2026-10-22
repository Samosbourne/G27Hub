using G27Hub.Core;

namespace G27Hub.Core.Tests;

public class ProfilesTests
{
    static string TempFile(string? content = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"g27hub-test-{Guid.NewGuid():N}.json");
        if (content != null) File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void MigratesPhase4ConfigIntoDesktop()
    {
        var path = TempFile("""{ "Range": 301, "Autocenter": 43, "AutocenterOn": false, "CloseToTray": false }""");
        var cfg = AppConfig.Load(path);
        Assert.Equal(301, cfg.Desktop.Wheel.Range);
        Assert.Equal(43, cfg.Desktop.Wheel.Autocenter);
        Assert.False(cfg.Desktop.Wheel.AutocenterOn);
        Assert.False(cfg.CloseToTray);

        cfg.Save();
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("\"Range\": 301,\r\n  \"Autocenter\"", text); // campos soltos não voltam
        Assert.Equal(301, AppConfig.Load(path).Desktop.Wheel.Range);
        File.Delete(path);
    }

    [Fact]
    public void GameProfileInheritsDesktopUntilSpecialSettings()
    {
        var cfg = AppConfig.Load(TempFile());
        var forza = cfg.AddGame(@"C:\Jogos\ForzaHorizon5.exe", "Forza Horizon 5");
        cfg.Desktop.Wheel.Range = 540;
        Assert.Equal(540, cfg.Effective(forza).Range);

        forza.UseOwnSettings = true;
        forza.Wheel.Range = 900;
        Assert.Equal(900, cfg.Effective(forza).Range);
        Assert.Equal(540, cfg.Effective(cfg.Desktop).Range);
    }

    [Fact]
    public void MatchesRunningProcessByExeName()
    {
        var cfg = AppConfig.Load(TempFile());
        var acc = cfg.AddGame(@"D:\Steam\acc\AC2-Win64-Shipping.exe", "ACC");
        Assert.Same(acc, cfg.MatchRunning(["explorer", "ac2-win64-shipping"]));
        Assert.Null(cfg.MatchRunning(["explorer", "chrome"]));
    }

    [Fact]
    public void DuplicateImportAndRemove()
    {
        var cfg = AppConfig.Load(TempFile());
        var game = cfg.AddGame(@"C:\x\game.exe", "Jogo");
        var copy = cfg.Duplicate(game);
        Assert.NotEqual(game.Id, copy.Id);
        Assert.Empty(copy.Exes);

        var imported = cfg.Import(AppConfig.ToJson(game));
        Assert.NotEqual(game.Id, imported.Id);
        Assert.Equal("Jogo", imported.Name);

        cfg.ActiveId = game.Id;
        Assert.True(cfg.Remove(game));
        Assert.Same(cfg.Desktop, cfg.Active);
        Assert.False(cfg.Remove(cfg.Desktop));
    }
}
