using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using G27Hub.Logging;

namespace G27Hub.Core;

/// <summary>
/// HidHide pelo HidHideCLI (não precisa de admin): esconde o G27 de todos os programas menos do G27 Hub.
/// </summary>
public static class HidHide
{
    public static string Cli => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nefarius Software Solutions", "HidHide", "x64", "HidHideCLI.exe");

    static string Run(params string[] args)
    {
        var psi = new ProcessStartInfo(Cli) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"HidHideCLI {string.Join(' ', args)} falhou ({p.ExitCode}): {output.Trim()}");
        return output;
    }

    public static bool IsCloaked() => Run("--cloak-state").Contains("--cloak-on");

    /// <summary>Caminhos de instância HID do G27 conectado (C29B ou C294), da lista de dispositivos de jogo.</summary>
    public static List<string> G27Paths() => ParseG27Paths(Run("--dev-gaming"));

    public static List<string> ParseG27Paths(string devGamingJson)
    {
        var paths = new List<string>();
        using var doc = JsonDocument.Parse(devGamingJson);
        foreach (var group in doc.RootElement.EnumerateArray())
            foreach (var d in group.GetProperty("devices").EnumerateArray())
                if (d.GetProperty("deviceInstancePath").GetString() is { } path &&
                    (path.Contains("VID_046D&PID_C29B", StringComparison.OrdinalIgnoreCase) || path.Contains("VID_046D&PID_C294", StringComparison.OrdinalIgnoreCase)))
                    paths.Add(path);
        return paths;
    }

    /// <summary>Libera o G27 Hub, esconde o G27 e liga o HidHide. Os jogos precisam ser reabertos.</summary>
    public static void HideG27()
    {
        var args = new List<string> { "--app-reg", Environment.ProcessPath! };
        foreach (var p in G27Paths()) { args.Add("--dev-hide"); args.Add(p); }
        args.Add("--cloak-on");
        Run([.. args]);
        AppLog.Ok("VJOY", $"G27 escondido dos jogos pelo HidHide ({(args.Count - 3) / 2} dispositivo(s)); o G27 Hub continua vendo");
    }

    /// <summary>Desliga o HidHide: o G27 volta a aparecer (a lista de escondidos fica, só não vale com ele desligado).</summary>
    public static void ShowG27()
    {
        Run("--cloak-off");
        AppLog.Ok("VJOY", "G27 visível de novo para os jogos (HidHide desligado)");
    }
}

/// <summary>
/// Volante virtual (vJoy, dispositivo 1) alimentado com o G27: X = volante, Y = acelerador, Z = freio, Rx = embreagem,
/// Ry = pedais combinados (acelerador − freio), botões 1–23 iguais ao G27, POV contínuo = direcional do câmbio.
/// Só manda ao vJoy o que mudou (cada chamada do vJoyInterface é uma ida ao driver).
/// </summary>
public sealed class VirtualWheel : IDisposable
{
    public const uint Id = 1;
    public const int Buttons = 24;
    const uint X = 0x30, Y = 0x31, Z = 0x32, Rx = 0x33, Ry = 0x34;
    static readonly uint[] AxesUsed = [X, Y, Z, Rx, Ry];

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "vJoy", "x64");
    /// <summary>Configuração do dispositivo 1 (precisa de admin): roda no script elevado.</summary>
    public static string ConfigArgs => $"1 -f -a x y z rx ry -b {Buttons} -s 1 -e all";

    readonly Dictionary<uint, int> _axes = [];
    readonly bool[] _buttons = new bool[Buttons + 1];
    int _pov = int.MinValue;
    long _max;
    bool _owned, _contPov;

    static bool _loaded;
    static bool Load()
    {
        if (_loaded) return true;
        var dll = Path.Combine(Dir, "vJoyInterface.dll");
        if (!File.Exists(dll) || !NativeLibrary.TryLoad(dll, out _)) return false;
        return _loaded = true;
    }

    /// <summary>True se o vJoy está instalado mas o dispositivo 1 ainda não tem os eixos/botões/POV/FFB do G27 Hub.</summary>
    public static bool NeedsConfig()
    {
        if (!Load() || !Native.vJoyEnabled()) return true;
        if (Native.GetVJDStatus(Id) == 3) return true; // não existe
        // ponytail: o vJoy 2.2.2 informa o POV contínuo (-s 1) como discreto; aceita qualquer um dos dois
        return Native.GetVJDButtonNumber(Id) < Buttons || Native.GetVJDContPovNumber(Id) + Native.GetVJDDiscPovNumber(Id) < 1 ||
               !Native.IsDeviceFfb(Id) || AxesUsed.Any(a => !Native.GetVJDAxisExist(Id, a));
    }

    /// <summary>Para o log: o que o dispositivo 1 tem hoje.</summary>
    public static string Describe() => !Load() ? "vJoyInterface.dll não encontrada"
        : $"estado {Native.GetVJDStatus(Id)}, {Native.GetVJDButtonNumber(Id)} botões, POV {Native.GetVJDContPovNumber(Id)} contínuo + {Native.GetVJDDiscPovNumber(Id)} discreto, " +
          $"FFB {(Native.IsDeviceFfb(Id) ? "sim" : "não")}, eixos {string.Join(" ", AxesUsed.Where(a => Native.GetVJDAxisExist(Id, a)).Select(a => $"0x{a:X2}"))}";

    public bool Start()
    {
        if (!Load() || !Native.vJoyEnabled()) { AppLog.Erro("VJOY", "vJoy não encontrado ou desligado"); return false; }
        var status = Native.GetVJDStatus(Id);
        if (status == 2) { AppLog.Erro("VJOY", "O vJoy 1 está sendo usado por outro programa (feche o vJoyFeeder ou similar)"); return false; }
        if (status != 0 && !Native.AcquireVJD(Id)) { AppLog.Erro("VJOY", $"Não consegui assumir o vJoy 1 (estado {status})"); return false; }
        _owned = true;
        Native.ResetVJD(Id);
        Native.GetVJDAxisMax(Id, X, out var max);
        _max = max;
        if (_max <= 0) _max = 32767;
        _contPov = Native.GetVJDContPovNumber(Id) > 0;
        AppLog.Ok("VJOY", $"Volante virtual (vJoy 1) ligado: {Native.GetVJDButtonNumber(Id)} botões, eixos até {_max}");
        return true;
    }

    /// <summary>Repassa o estado do G27 ao vJoy com as curvas do perfil. Chamado na thread de leitura do volante.</summary>
    public void Feed(G27State s, WheelSettings w)
    {
        if (!_owned) return;
        double steer = s.Axes.TryGetValue("X", out var x) ? x.Normalized * 2 - 1 : 0;
        double throttle = Curve(s.Throttle, w.ThrottleSensitivity), brake = Curve(s.Brake, w.BrakeSensitivity), clutch = Curve(s.Clutch, w.ClutchSensitivity);
        Axis(X, (Curve(steer, w.SteerSensitivity) + 1) / 2);
        Axis(Y, throttle);
        Axis(Z, brake);
        Axis(Rx, clutch);
        Axis(Ry, (throttle - brake + 1) / 2);
        for (int b = 1; b <= Buttons; b++)
        {
            bool on = s.Buttons.Contains(b);
            if (on != _buttons[b] && Native.SetBtn(on, Id, (byte)b)) _buttons[b] = on;
        }
        int pov = !(s.Hat is >= 0 and < 8) ? -1 // solto
            : _contPov ? s.Hat * 4500           // centésimos de grau
            : s.Hat / 2;                        // discreto: 0 cima, 1 direita, 2 baixo, 3 esquerda (diagonal vai para a seguinte)
        if (pov != _pov && (_contPov ? Native.SetContPov(pov, Id, 1) : Native.SetDiscPov(pov, Id, 1))) _pov = pov;
    }

    void Axis(uint axis, double value01)
    {
        int v = (int)Math.Round(Math.Clamp(value01, 0, 1) * _max);
        if (_axes.TryGetValue(axis, out var old) && old == v) return;
        if (Native.SetAxis(v, Id, axis)) _axes[axis] = v;
    }

    /// <summary>
    /// Curva de sensibilidade estilo G HUB: 50 = linear, menos = mais suave no começo, mais = mais rápido no começo.
    /// Vale para -1..1 (volante) e 0..1 (pedais).
    /// </summary>
    public static double Curve(double v, int sensitivity)
    {
        double gamma = Math.Pow(3, (50 - Math.Clamp(sensitivity, 0, 100)) / 50.0); // 0 → 3, 50 → 1, 100 → 1/3
        return Math.Sign(v) * Math.Pow(Math.Abs(v), gamma);
    }

    public void Dispose()
    {
        if (!_owned) return;
        _owned = false;
        Native.ResetVJD(Id);
        Native.RelinquishVJD(Id);
        AppLog.Info("VJOY", "Volante virtual (vJoy 1) desligado");
    }

    static class Native
    {
        const string D = "vJoyInterface.dll";
        [DllImport(D)] public static extern bool vJoyEnabled();
        [DllImport(D)] public static extern int GetVJDStatus(uint id); // 0 livre p/ nós, 1 livre, 2 ocupado, 3 não existe
        [DllImport(D)] public static extern bool AcquireVJD(uint id);
        [DllImport(D)] public static extern void RelinquishVJD(uint id);
        [DllImport(D)] public static extern bool ResetVJD(uint id);
        [DllImport(D)] public static extern bool GetVJDAxisExist(uint id, uint axis);
        [DllImport(D)] public static extern bool GetVJDAxisMax(uint id, uint axis, out int max); // LONG do Windows = 32 bits
        [DllImport(D)] public static extern int GetVJDButtonNumber(uint id);
        [DllImport(D)] public static extern int GetVJDContPovNumber(uint id);
        [DllImport(D)] public static extern int GetVJDDiscPovNumber(uint id);
        [DllImport(D)] public static extern bool SetDiscPov(int value, uint id, byte n);
        [DllImport(D)] public static extern bool IsDeviceFfb(uint id);
        [DllImport(D)] public static extern bool SetAxis(int value, uint id, uint axis);
        [DllImport(D)] public static extern bool SetBtn(bool value, uint id, byte n);
        [DllImport(D)] public static extern bool SetContPov(int value, uint id, byte n);
    }
}
