using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace G27Hub.Core;

public sealed record InstalledGame(CompatEntry Game, string ExePath, string Store);

/// <summary>Acha jogos da base instalados no PC: bibliotecas da Steam, Epic e Xbox/Microsoft Store (só leitura).</summary>
public static partial class GameScanner
{
    public static List<InstalledGame> Scan(CompatDb db)
    {
        var wanted = db.Games.SelectMany(g => g.Exes.Select(e => (Exe: e.ToLowerInvariant(), Game: g)))
            .GroupBy(x => x.Exe).ToDictionary(x => x.Key, x => x.First().Game);
        var found = new Dictionary<string, InstalledGame>();
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true };

        foreach (var (root, store) in Roots())
        {
            if (!Directory.Exists(root)) continue;
            foreach (var exe in Directory.EnumerateFiles(root, "*.exe", options))
                if (wanted.TryGetValue(Path.GetFileName(exe).ToLowerInvariant(), out var g) && !found.ContainsKey(g.Id))
                    found[g.Id] = new InstalledGame(g, Path.GetFullPath(exe), store);
        }
        return found.Values.OrderBy(x => x.Game.Name).ToList();
    }

    static IEnumerable<(string Dir, string Store)> Roots()
    {
        foreach (var dir in SteamCommonDirs()) yield return (dir, "Steam");

        // Epic: um .item (JSON) por jogo com a pasta de instalação
        var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (Directory.Exists(manifests))
            foreach (var item in Directory.EnumerateFiles(manifests, "*.item"))
            {
                string? dir = null;
                try { dir = JsonDocument.Parse(File.ReadAllText(item)).RootElement.GetProperty("InstallLocation").GetString(); }
                catch { } // manifesto estranho: pula
                if (dir != null) yield return (dir, "Epic");
            }

        // Xbox / Microsoft Store (Game Pass): \XboxGames em cada disco
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            yield return (Path.Combine(drive.RootDirectory.FullName, "XboxGames"), "Xbox");
    }

    /// <summary>steamapps\common da pasta da Steam e de cada biblioteca extra (libraryfolders.vdf).</summary>
    public static IEnumerable<string> SteamCommonDirs()
    {
        if (Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") is not string steam) yield break;
        steam = Path.GetFullPath(steam); // o registro guarda com barras "/"
        var libs = new List<string> { steam };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf)) libs.AddRange(ParseLibraryFolders(File.ReadAllText(vdf)));
        foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
            yield return Path.Combine(lib, "steamapps", "common");
    }

    /// <summary>Caminhos ("path" "D:\\SteamLibrary") de steamapps/libraryfolders.vdf.</summary>
    public static IEnumerable<string> ParseLibraryFolders(string vdf) =>
        PathRegex().Matches(vdf).Select(m => m.Groups[1].Value.Replace(@"\\", @"\"));

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex PathRegex();
}

/// <summary>Controles de Xbox conectados (XInput). O G27 não é XInput, então não conta.</summary>
public static class XboxControllers
{
    public static int Count()
    {
        int n = 0;
        try
        {
            for (uint i = 0; i < 4; i++)
                if (XInputGetState(i, out _) == 0) n++;
        }
        catch (DllNotFoundException) { } // Windows sem XInput 1.4: nenhum controle
        return n;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_STATE { public uint Packet; public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }

    [DllImport("xinput1_4.dll")] static extern uint XInputGetState(uint index, out XINPUT_STATE state);
}
