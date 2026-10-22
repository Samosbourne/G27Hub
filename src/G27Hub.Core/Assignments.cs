namespace G27Hub.Core;

public enum ActionKind { None, Key, Macro, Media, Program, Website, Profile }
public enum MacroMode { Once, RepeatWhileHeld, Toggle }

/// <summary>Um passo de macro: espera DelayMs e aperta (Down) ou solta a tecla.</summary>
public sealed class MacroStep
{
    public int Vk { get; set; }
    public bool Down { get; set; }
    public int DelayMs { get; set; }
}

/// <summary>O que um botão do volante faz (aba Atribuições).</summary>
public sealed class ButtonAction
{
    public ActionKind Kind { get; set; }
    public string Keys { get; set; } = "";        // Key: "Ctrl+Shift+R"
    public List<MacroStep> Steps { get; set; } = [];
    public MacroMode Mode { get; set; }
    public string Media { get; set; } = "";       // um de KeyNames.Media
    public string Path { get; set; } = "";        // Program
    public string Url { get; set; } = "";         // Website
    public string ProfileId { get; set; } = "";   // Profile

    public string Summary(AppConfig? cfg = null) => Kind switch
    {
        ActionKind.Key => Keys,
        ActionKind.Macro => $"Macro ({Steps.Count(s => s.Down)} teclas{Mode switch { MacroMode.RepeatWhileHeld => ", repete", MacroMode.Toggle => ", liga/desliga", _ => "" }})",
        ActionKind.Media => Media,
        ActionKind.Program => $"Abrir {System.IO.Path.GetFileNameWithoutExtension(Path)}",
        ActionKind.Website => Url,
        ActionKind.Profile => $"Perfil: {cfg?.Profiles.FirstOrDefault(p => p.Id == ProfileId)?.Name ?? "?"}",
        _ => "Padrão",
    };
}

/// <summary>Pedal como tecla: passou de Threshold% = tecla apertada (ex.: embreagem vira freio de mão).</summary>
public sealed class PedalKey
{
    public bool On { get; set; }
    public int Threshold { get; set; } = 60;
    public string Keys { get; set; } = "";
}

/// <summary>Câmbio H mandando teclas de marcha acima/abaixo, para jogos só com câmbio sequencial.</summary>
public sealed class SequentialShift
{
    public bool On { get; set; }
    public string Up { get; set; } = "E";
    public string Down { get; set; } = "Q";
}

public interface IAssignmentHost
{
    void SwitchProfile(string profileId);
    void CycleRange();
}

/// <summary>
/// Transforma o estado do G27 em teclas, macros etc. conforme o perfil. Roda na thread de leitura do volante,
/// então funciona com o app minimizado e com o jogo na frente. O jogo continua vendo o botão (Modo Padrão).
/// </summary>
public sealed class ButtonEngine(IKeySink keys, IAssignmentHost host)
{
    /// <summary>Números virtuais para as 4 direções do direcional (hat).</summary>
    public const int HatUp = 101, HatRight = 102, HatDown = 103, HatLeft = 104;

    readonly Dictionary<int, ButtonAction> _held = [];
    readonly Dictionary<int, CancellationTokenSource> _loops = [];
    readonly bool[] _pedalDown = new bool[3];
    HashSet<int> _prev = [];
    bool _shift, _comboWas;
    int? _seqGear;
    long _lastShortcut = long.MinValue / 2;

    /// <summary>Intervalo mínimo entre duas trocas pelo atalho: o contato dos botões "treme" ao apertar os dois juntos.</summary>
    public long ShortcutCooldownMs { get; set; } = 600;

    /// <summary>Fila das teclas do câmbio sequencial (exposta para os testes esperarem).</summary>
    public Task SequentialQueue { get; private set; } = Task.CompletedTask;

    public static IEnumerable<int> Pressed(G27State s) => s.Buttons.Concat(HatButtons(s.Hat));

    static IEnumerable<int> HatButtons(int hat)
    {
        if (hat is 7 or 0 or 1) yield return HatUp;
        if (hat is 1 or 2 or 3) yield return HatRight;
        if (hat is 3 or 4 or 5) yield return HatDown;
        if (hat is 5 or 6 or 7) yield return HatLeft;
    }

    /// <param name="paused">Atribuições pausadas (ex.: G27 Hub em foco); o atalho de rotação continua valendo.</param>
    public void Process(G27State s, Profile p, AppConfig cfg, bool paused = false)
    {
        var now = Pressed(s).ToHashSet();

        // atalho de rotação: os dois vermelhos de cima do volante juntos
        var combo = cfg.RotationShortcut && now.Contains(G27Buttons.WheelLeft[0]) && now.Contains(G27Buttons.WheelRight[0]);
        if (combo && !_comboWas && Environment.TickCount64 - _lastShortcut >= ShortcutCooldownMs)
        {
            _lastShortcut = Environment.TickCount64;
            host.CycleRange();
        }
        _comboWas = combo;

        if (paused || !p.AssignmentsOn)
        {
            if (_held.Count > 0 || _loops.Count > 0) ReleaseAll();
            _prev = now;
            return;
        }
        foreach (var b in _prev.Except(now)) Release(b, p);
        foreach (var b in now.Except(_prev)) Press(b, p);
        Pedals(s, p);
        Sequential(s, p);
        _prev = now;
    }

    /// <summary>Solta tudo (troca de perfil, atribuições desligadas, app fechando).</summary>
    public void ReleaseAll()
    {
        foreach (var (_, a) in _held) End(a, -1);
        _held.Clear();
        foreach (var cts in _loops.Values) cts.Cancel();
        _loops.Clear();
        _shift = false;
        _prev = [];
        _seqGear = null;
        for (int i = 0; i < 3; i++) _pedalDown[i] = false;
    }

    void Press(int b, Profile p)
    {
        if (b == p.ShiftButton) { _shift = true; return; }
        var a = _shift && p.ShiftActions.TryGetValue(b, out var sa) ? sa : p.Actions.GetValueOrDefault(b);
        if (a == null || a.Kind == ActionKind.None) return;
        _held[b] = a;
        Start(b, a);
    }

    void Release(int b, Profile p)
    {
        if (b == p.ShiftButton) { _shift = false; return; }
        if (_held.Remove(b, out var a)) End(a, b);
    }

    void Start(int b, ButtonAction a)
    {
        switch (a.Kind)
        {
            case ActionKind.Key when KeyNames.Parse(a.Keys) is { } vks:
                foreach (var vk in vks) keys.Down(vk);
                break;
            case ActionKind.Media when KeyNames.Vk(a.Media) is { } mvk:
                keys.Down(mvk);
                keys.Up(mvk);
                break;
            case ActionKind.Program when a.Path.Length > 0:
                keys.Launch(a.Path);
                break;
            case ActionKind.Website when a.Url.Length > 0:
                keys.Launch(a.Url.Contains("://") ? a.Url : "https://" + a.Url);
                break;
            case ActionKind.Profile:
                host.SwitchProfile(a.ProfileId);
                break;
            case ActionKind.Macro:
                if (a.Mode == MacroMode.Toggle && _loops.Remove(b, out var running)) { running.Cancel(); break; }
                var cts = new CancellationTokenSource();
                if (a.Mode != MacroMode.Once) _loops[b] = cts;
                var steps = a.Steps.ToArray();
                var repeat = a.Mode != MacroMode.Once;
                Task.Run(() => PlayMacro(steps, repeat, cts.Token));
                break;
        }
    }

    void End(ButtonAction a, int b)
    {
        if (a.Kind == ActionKind.Key && KeyNames.Parse(a.Keys) is { } vks)
            foreach (var vk in vks.Reverse()) keys.Up(vk);
        if (a is { Kind: ActionKind.Macro, Mode: MacroMode.RepeatWhileHeld } && _loops.Remove(b, out var cts)) cts.Cancel();
    }

    void PlayMacro(MacroStep[] steps, bool repeat, CancellationToken ct)
    {
        var down = new HashSet<int>();
        try
        {
            do
            {
                foreach (var st in steps)
                {
                    if (st.DelayMs > 0 && ct.WaitHandle.WaitOne(st.DelayMs)) return;
                    if (ct.IsCancellationRequested) return;
                    if (st.Down) { keys.Down(st.Vk); down.Add(st.Vk); }
                    else { keys.Up(st.Vk); down.Remove(st.Vk); }
                }
                if (repeat && steps.All(s => s.DelayMs == 0) && ct.WaitHandle.WaitOne(30)) return; // não travar a CPU
            } while (repeat && !ct.IsCancellationRequested);
        }
        finally
        {
            foreach (var vk in down) keys.Up(vk); // nunca deixar tecla presa
        }
    }

    void Pedals(G27State s, Profile p)
    {
        double[] values = [s.Clutch, s.Brake, s.Throttle];
        for (int i = 0; i < 3 && i < p.Pedals.Count; i++)
        {
            var pk = p.Pedals[i];
            if (!pk.On || KeyNames.Parse(pk.Keys) is not { } vks) continue;
            var v = values[i] * 100;
            var on = _pedalDown[i] ? v > pk.Threshold - 5 : v >= pk.Threshold; // 5% de folga para não piscar
            if (on == _pedalDown[i]) continue;
            _pedalDown[i] = on;
            if (on) foreach (var vk in vks) keys.Down(vk);
            else foreach (var vk in vks.Reverse()) keys.Up(vk);
        }
    }

    void Sequential(G27State s, Profile p)
    {
        if (!p.Sequential.On) return;
        var gear = s.Gear;
        if (gear == "N") return; // ponto morto não muda a marcha do jogo
        var target = gear == "R" ? -1 : int.Parse(gear);
        if (_seqGear is not { } current) { _seqGear = target; return; } // primeira leitura: só sincroniza
        if (target == current) return;
        _seqGear = target;
        var vks = KeyNames.Parse(target > current ? p.Sequential.Up : p.Sequential.Down);
        if (vks == null) return;
        var taps = Math.Abs(target - current);
        SequentialQueue = SequentialQueue.ContinueWith(_ =>
        {
            for (int i = 0; i < taps; i++)
            {
                foreach (var vk in vks) keys.Down(vk);
                Thread.Sleep(40);
                foreach (var vk in vks.Reverse()) keys.Up(vk);
                Thread.Sleep(40);
            }
        });
    }
}
