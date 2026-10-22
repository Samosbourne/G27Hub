using G27Hub.Core;

namespace G27Hub.Core.Tests;

public class ButtonEngineTests
{
    sealed class FakeSink : IKeySink, IAssignmentHost
    {
        public readonly List<string> Log = [];
        public void Down(int vk) { lock (Log) Log.Add("+" + KeyNames.Name(vk)); }
        public void Up(int vk) { lock (Log) Log.Add("-" + KeyNames.Name(vk)); }
        public void Launch(string what) { lock (Log) Log.Add("abrir " + what); }
        public void SwitchProfile(string id) { lock (Log) Log.Add("perfil " + id); }
        public void CycleRange() { lock (Log) Log.Add("rotação"); }
    }

    static readonly AppConfig Cfg = new();

    static G27State State(int[] buttons, int hat = -1, double clutch = 0, double brake = 0, double gas = 0) =>
        new(new Dictionary<string, AxisValue>
        {
            ["Y"] = new((int)Math.Round((1 - clutch) * 255), 0, 255),
            ["Rz"] = new((int)Math.Round((1 - brake) * 255), 0, 255),
            ["Z"] = new((int)Math.Round((1 - gas) * 255), 0, 255),
        }, buttons, hat, 900);

    [Fact]
    public void KeyIsHeldWhileButtonIsHeld_ComboReleasedInReverse()
    {
        var f = new FakeSink();
        var e = new ButtonEngine(f, f);
        var p = new Profile();
        p.Actions[7] = new ButtonAction { Kind = ActionKind.Key, Keys = "Ctrl+Shift+R" };

        e.Process(State([7]), p, Cfg);
        Assert.Equal(["+Ctrl", "+Shift", "+R"], f.Log);
        e.Process(State([7]), p, Cfg); // segurando: nada novo
        e.Process(State([]), p, Cfg);
        Assert.Equal(["+Ctrl", "+Shift", "+R", "-R", "-Shift", "-Ctrl"], f.Log);
    }

    [Fact]
    public void ShiftLayerAndHatButtons()
    {
        var f = new FakeSink();
        var e = new ButtonEngine(f, f);
        var p = new Profile { ShiftButton = 6 };
        p.Actions[ButtonEngine.HatUp] = new ButtonAction { Kind = ActionKind.Key, Keys = "A" };
        p.ShiftActions[ButtonEngine.HatUp] = new ButtonAction { Kind = ActionKind.Key, Keys = "B" };

        e.Process(State([], hat: 0), p, Cfg);
        e.Process(State([]), p, Cfg);
        e.Process(State([6]), p, Cfg);          // segura o shift
        e.Process(State([6], hat: 7), p, Cfg);  // cima-esquerda conta como cima
        e.Process(State([6]), p, Cfg);
        Assert.Equal(["+A", "-A", "+B", "-B"], f.Log);
    }

    [Fact]
    public void PedalKeyHasHysteresis()
    {
        var f = new FakeSink();
        var e = new ButtonEngine(f, f);
        var p = new Profile();
        p.Pedals[0] = new PedalKey { On = true, Threshold = 60, Keys = "Espaço" };

        e.Process(State([], clutch: 0.59), p, Cfg);
        e.Process(State([], clutch: 0.61), p, Cfg); // apertou
        e.Process(State([], clutch: 0.57), p, Cfg); // ainda dentro da folga
        e.Process(State([], clutch: 0.50), p, Cfg); // soltou
        Assert.Equal(["+Espaço", "-Espaço"], f.Log);
    }

    [Fact]
    public async Task SequentialShifterSendsTapsIgnoringNeutral()
    {
        var f = new FakeSink();
        var e = new ButtonEngine(f, f);
        var p = new Profile { Sequential = new SequentialShift { On = true, Up = "E", Down = "Q" } };
        int g(int gear) => G27Buttons.Gears[gear - 1];

        e.Process(State([g(1)]), p, Cfg);  // sincroniza na 1ª
        e.Process(State([]), p, Cfg);      // ponto morto: nada
        e.Process(State([g(3)]), p, Cfg);  // 1 → 3: duas vezes E
        e.Process(State([G27Buttons.Reverse]), p, Cfg); // 3 → R(-1): quatro vezes Q
        await e.SequentialQueue;
        Assert.Equal(["+E", "-E", "+E", "-E", "+Q", "-Q", "+Q", "-Q", "+Q", "-Q", "+Q", "-Q"], f.Log);
    }

    [Fact]
    public async Task MacroOnceAndOtherActions()
    {
        var f = new FakeSink();
        var e = new ButtonEngine(f, f);
        var p = new Profile();
        p.Actions[9] = new ButtonAction { Kind = ActionKind.Macro, Steps = [new() { Vk = 'H', Down = true }, new() { Vk = 'H', Down = false, DelayMs = 10 }, new() { Vk = 'I', Down = true }, new() { Vk = 'I' }] };
        p.Actions[10] = new ButtonAction { Kind = ActionKind.Media, Media = "Volume+" };
        p.Actions[11] = new ButtonAction { Kind = ActionKind.Profile, ProfileId = "x" };

        e.Process(State([9]), p, Cfg);
        for (int i = 0; i < 50 && f.Log.Count < 4; i++) await Task.Delay(20);
        e.Process(State([10, 11]), p, Cfg);
        Assert.Equal(["+H", "-H", "+I", "-I", "+Volume+", "-Volume+", "perfil x"], f.Log);
    }

    [Fact]
    public void RotationShortcutAndAssignmentsOff()
    {
        var f = new FakeSink();
        var e = new ButtonEngine(f, f);
        var p = new Profile { AssignmentsOn = false };
        p.Actions[7] = new ButtonAction { Kind = ActionKind.Key, Keys = "A" };
        var both = new[] { G27Buttons.WheelLeft[0], G27Buttons.WheelRight[0] };

        e.Process(State(both), p, Cfg);
        e.Process(State(both), p, Cfg); // segurando: só uma vez
        Assert.Equal(["rotação"], f.Log); // atribuições desligadas, atalho global continua
    }

    [Fact]
    public void PausedSendsNoKeysButShortcutStillWorks()
    {
        var f = new FakeSink();
        var e = new ButtonEngine(f, f) { ShortcutCooldownMs = 0 };
        var p = new Profile();
        p.Actions[G27Buttons.WheelLeft[0]] = new ButtonAction { Kind = ActionKind.Key, Keys = "A" };
        var both = new[] { G27Buttons.WheelLeft[0], G27Buttons.WheelRight[0] };

        e.Process(State(both), p, Cfg, paused: true);
        e.Process(State([]), p, Cfg, paused: true);
        e.Process(State(both), p, Cfg, paused: true);
        Assert.Equal(["rotação", "rotação"], f.Log);
    }

    [Fact]
    public void ShortcutIgnoresContactBounce()
    {
        var f = new FakeSink();
        var e = new ButtonEngine(f, f); // 600 ms padrão
        var both = new[] { G27Buttons.WheelLeft[0], G27Buttons.WheelRight[0] };
        foreach (var s in new[] { both, [G27Buttons.WheelLeft[0]], both, [], both })
            e.Process(State(s), new Profile(), Cfg);
        Assert.Equal(["rotação"], f.Log); // tremeu 3 vezes em poucos ms: troca uma vez só
    }

    [Theory]
    [InlineData("Volume-", false)]
    [InlineData("Volume+", false)]
    [InlineData("Mudo", false)]
    [InlineData("Play/Pausa", false)]
    [InlineData("C", true)]
    [InlineData("Espaço", true)]
    public void MediaKeysGoByVirtualKey(string key, bool scan) =>
        Assert.Equal(scan, SendInputSink.UsesScanCode(KeyNames.Vk(key)!.Value));

    [Theory]
    [InlineData("Ctrl+Shift+R", "Ctrl+Shift+R")]
    [InlineData("num+", "Num+")]
    [InlineData("f12", "F12")]
    [InlineData("Volume+", "Volume+")]
    public void KeyNamesRoundTrip(string input, string expected) =>
        Assert.Equal(expected, KeyNames.Format(KeyNames.Parse(input)!));
}
