using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using G27Hub.Logging;

namespace G27Hub.Core;

/// <summary>
/// BeamNG.drive: o mapa padrão do G27 que vem no jogo (settings/inputmaps/c29b046d.json) foi feito com o LGS,
/// que reorganiza pedais e botões. Sem o LGS (Modo Padrão) o acelerador vira embreagem e as marchas ficam erradas.
/// Isto escreve o .diff do usuário (o jeito do próprio BeamNG de guardar mudanças) com o layout do G27 sem LGS,
/// mantendo o resto (ex.: o force feedback da direção que o John já ajustou) e com backup para desfazer.
/// </summary>
public static class BeamNgFix
{
    public const string Marker = "g27hub-layout-nativo-v1";
    const string FileName = "c29b046d.diff";
    const string BackupName = "c29b046d.diff.antes-do-g27hub";
    const string NoBackupName = "c29b046d.diff.criado-pelo-g27hub";

    /// <summary>Ligações do mapa do LGS que, sem o LGS, caem em outras peças (ex.: botão 15 = 4ª marcha trocava a câmera).</summary>
    static readonly (string Control, string Action)[] RemoveFromDefault =
    [
        ("button8", "gear1"), ("button9", "gear2"), ("button10", "gear3"), ("button11", "gear4"), ("button12", "gear5"), ("button13", "gear6"), ("button14", "gearR"),
        ("button15", "switch_camera_next"), ("button17", "center_camera"), ("button17", "menu_item_select"),
        ("yaxis", "accelerate"), ("slider2", "clutch"),
    ];

    /// <summary>Layout do G27 nativo (0-based, como o BeamNG conta): marchas 13–18 → button12–17, ré 23 → button22.</summary>
    static JsonArray OurBindings()
    {
        var list = new JsonArray
        {
            new JsonObject { ["control"] = "zaxis", ["action"] = "accelerate", ["isInverted"] = true },
            new JsonObject { ["control"] = "yaxis", ["action"] = "clutch", ["isInverted"] = true },
        };
        for (int g = 0; g < 6; g++) list.Add(new JsonObject { ["control"] = $"button{G27Buttons.Gears[g] - 1}", ["action"] = $"gear{g + 1}" });
        list.Add(new JsonObject { ["control"] = $"button{G27Buttons.Reverse - 1}", ["action"] = "gearR" });
        return list;
    }

    static readonly HashSet<string> OurControls = ["zaxis", "yaxis", "slider2", .. Enumerable.Range(8, 10).Select(i => $"button{i}"), "button22"];

    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BeamNG", "BeamNG.drive", "current", "settings", "inputmaps");

    public static bool IsInstalled(string? dir = null) => Directory.Exists(Path.GetDirectoryName(dir ?? DefaultDir));

    public static bool IsApplied(string? dir = null)
    {
        var path = Path.Combine(dir ?? DefaultDir, FileName);
        return File.Exists(path) && File.ReadAllText(path).Contains(Marker);
    }

    public static void Apply(string? dir = null)
    {
        dir ??= DefaultDir;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, FileName);
        JsonObject root;
        if (File.Exists(path))
        {
            if (!File.Exists(Path.Combine(dir, BackupName)) && !File.ReadAllText(path).Contains(Marker))
                File.Copy(path, Path.Combine(dir, BackupName));
            root = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? new JsonObject();
        }
        else
        {
            File.WriteAllText(Path.Combine(dir, NoBackupName), "o c29b046d.diff não existia antes do G27 Hub; desfazer = apagar");
            root = new JsonObject { ["devicetype"] = "wheel", ["vidpid"] = "C29B046D", ["version"] = 1 };
        }

        // mantém as ligações do usuário que não são de pedal/marcha (ex.: direção com o FFB ajustado)
        var bindings = new JsonArray();
        foreach (var b in root["bindings"]?.AsArray() ?? [])
            if (b?["control"]?.GetValue<string>() is not { } c || !OurControls.Contains(c))
                bindings.Add(b?.DeepClone());
        foreach (var b in OurBindings()) bindings.Add(b!.DeepClone());

        var removed = new JsonArray();
        foreach (var (control, action) in RemoveFromDefault) removed.Add(new JsonObject { ["control"] = control, ["action"] = action });

        root["bindings"] = bindings;
        root["removed"] = removed;
        root["g27hub"] = Marker;
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        AppLog.Ok("JOGO", $"BeamNG: controles do G27 sem LGS gravados em {path} (backup ao lado)");
    }

    public static void Undo(string? dir = null)
    {
        dir ??= DefaultDir;
        var path = Path.Combine(dir, FileName);
        var backup = Path.Combine(dir, BackupName);
        var created = Path.Combine(dir, NoBackupName);
        if (File.Exists(backup)) { File.Copy(backup, path, overwrite: true); File.Delete(backup); }
        else if (File.Exists(created)) { File.Delete(path); File.Delete(created); }
        AppLog.Ok("JOGO", "BeamNG: controles voltaram ao que eram antes do G27 Hub");
    }
}
