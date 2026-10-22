using System.Text.Json.Nodes;
using G27Hub.Core;

namespace G27Hub.Core.Tests;

public class BeamNgFixTests
{
    // o .diff que estava no PC do John: só a direção com FFB ajustado (feito na época do LGS)
    const string JohnsDiff = """
        { "bindings":[ { "action":"steering", "angle":900, "control":"xaxis", "ffb":{ "forceCoef":370 }, "isForceEnabled":true, "isForceInverted":true } ],
          "devicetype":"wheel", "guid":"{C29B046D-0000-0000-0000-504944564944}", "name":"Logitech G27 Racing Wheel USB", "vendorName":"Logitech", "version":1, "vidpid":"C29B046D" }
        """;

    static string TempDir() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"g27hub-beamng-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public void ApplyKeepsUserSteeringAndFixesPedalsAndGears()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, "c29b046d.diff"), JohnsDiff);

        BeamNgFix.Apply(dir);
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "c29b046d.diff")))!;
        var bindings = root["bindings"]!.AsArray().Select(b => $"{b!["control"]}={b["action"]}").ToList();

        Assert.Contains("xaxis=steering", bindings);                       // FFB do John mantido
        Assert.Equal(370, (int)root["bindings"]![0]!["ffb"]!["forceCoef"]!);
        Assert.Contains("zaxis=accelerate", bindings);
        Assert.Contains("yaxis=clutch", bindings);
        Assert.Contains("button12=gear1", bindings);                       // botão 13 (1-based) = 1ª
        Assert.Contains("button17=gear6", bindings);
        Assert.Contains("button22=gearR", bindings);                       // botão 23 = ré
        var removed = root["removed"]!.AsArray().Select(b => $"{b!["control"]}={b["action"]}").ToList();
        Assert.Contains("yaxis=accelerate", removed);
        Assert.Contains("button8=gear1", removed);
        Assert.True(BeamNgFix.IsApplied(dir));

        BeamNgFix.Apply(dir); // de novo: não duplica nem estraga o backup
        Assert.Equal(bindings.Count, JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "c29b046d.diff")))!["bindings"]!.AsArray().Count);

        BeamNgFix.Undo(dir);
        Assert.Equal(JohnsDiff, File.ReadAllText(Path.Combine(dir, "c29b046d.diff")));
        Assert.False(BeamNgFix.IsApplied(dir));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void ApplyWithoutPreviousDiffAndUndoDeletes()
    {
        var dir = TempDir();
        BeamNgFix.Apply(dir);
        Assert.True(BeamNgFix.IsApplied(dir));
        BeamNgFix.Undo(dir);
        Assert.False(File.Exists(Path.Combine(dir, "c29b046d.diff")));
        Directory.Delete(dir, true);
    }
}
