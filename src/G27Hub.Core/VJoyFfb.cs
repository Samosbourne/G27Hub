using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using G27Hub.Logging;

namespace G27Hub.Core;

/// <summary>
/// Fase 8c: force feedback no Modo Compatibilidade Total. O jogo manda efeitos para o vJoy; o vJoyInterface entrega os
/// pacotes PID aqui; cada um vira a chamada equivalente do IDirectInputEffectDriver da nossa g27ffb.dll (x64), carregada
/// dentro do app. Assim a força é calculada pelo mesmo motor (e com os mesmos ajustes da aba Força) do Modo Padrão.
/// Estruturas do vJoy: SDK MIT (BrunnerInnovation/vJoy, inc/vjoyinterface.h).
/// </summary>
public sealed unsafe class VJoyFfb : IDisposable
{
    const uint DieffObjectOffsets = 0x02, DieffCartesian = 0x10, DieffPolar = 0x20, Infinite = 0xFFFFFFFF;
    const uint DisffcReset = 0x01, DisffcStopAll = 0x02, DisffcPause = 0x04, DisffcContinue = 0x08, DisffcOn = 0x10, DisffcOff = 0x20;

    static VJoyFfb? _current; // o vJoy só aceita um callback por processo; ele chama o objeto em uso
    static bool _registered;

    nint _drv;
    string _path = "", _dll = "";
    bool _disposed;
    long _lastPacket;
    Timer? _idle;
    /// <summary>Sem pacote do jogo por este tempo, o motor para e a mola do volante volta (sem jogo o volante não fica solto).</summary>
    const int IdleMs = 15_000;
    nint* _vt;
    readonly byte* _buf = (byte*)NativeMemory.AllocZeroed(512); // DIEFFECT + eixos + direção + envelope + parâmetros
    readonly Dictionary<uint, Eff> _effects = [];
    readonly object _lock = new();
    int _packets;
    readonly HashSet<uint> _seen = [];
    static readonly string[] TypeNames = ["constante", "rampa", "quadrada", "senoidal", "triangular", "dente-de-serra ↑", "dente-de-serra ↓", "mola", "amortecedor", "inércia", "atrito"];

    sealed class Eff
    {
        public uint TypeId = uint.MaxValue, Handle, Duration = Infinite, Gain = 10000, StartDelay;
        public bool Polar = true, Playing;
        public int Direction = 9000, DirX = 1; // centésimos de grau (90° = direita) ou X cartesiano
        public bool HasEnv;
        public uint AttackLevel, AttackTime, FadeLevel, FadeTime;
        public int Magnitude, RampStart, RampEnd, Offset, PosCoef, NegCoef, DeadBand;
        public uint PerMagnitude, Phase, Period = 100_000, PosSat = 10000, NegSat = 10000;
    }

    /// <summary>Liga o motor da g27ffb.dll no G27 (caminho HID) e passa a receber o FFB do vJoy 1.</summary>
    public static VJoyFfb Start(string g27DevicePath, string ffbDll)
    {
        var f = new VJoyFfb { _path = g27DevicePath, _dll = ffbDll };
        f._idle = new Timer(_ => f.CheckIdle(), null, 2000, 2000);
        lock (typeof(VJoyFfb)) _current = f;
        if (!_registered)
        {
            Native.FfbRegisterGenCB(&OnPacket, 0);
            _registered = true;
        }
        AppLog.Ok("FFB", "Force feedback do vJoy pronto: o motor liga quando o jogo mandar força");
        return f;
    }

    void CreateDriver(string path, string dll)
    {
        var lib = NativeLibrary.Load(dll);
        var getClass = (delegate* unmanaged<Guid*, Guid*, nint*, int>)NativeLibrary.GetExport(lib, "DllGetClassObject");
        Guid clsid = new(FfbRegistration.Clsid), iidFactory = new("00000001-0000-0000-C000-000000000046"), iidDrv = new("02538130-898F-11D0-9AD0-00A0C9A06E35");
        nint factory;
        Check(getClass(&clsid, &iidFactory, &factory), "DllGetClassObject");
        var fvt = *(nint**)factory;
        nint drv;
        int hr = ((delegate* unmanaged<nint, nint, Guid*, nint*, int>)fvt[3])(factory, 0, &iidDrv, &drv); // CreateInstance
        ((delegate* unmanaged<nint, uint>)fvt[2])(factory); // Release
        Check(hr, "CreateInstance");
        _drv = drv;
        _vt = *(nint**)drv;

        // DIHIDFFINITINFO (x64): dwSize, pwszDeviceInterface, GuidInstance
        var info = stackalloc byte[32];
        fixed (char* p = path)
        {
            *(uint*)info = 32;
            *(char**)(info + 8) = p;
            Check(((delegate* unmanaged<nint, uint, uint, uint, uint, void*, int>)_vt[3])(_drv, 0x0800, 0, 1, 0, info), "DeviceID");
        }
        Command(DisffcReset | DisffcOn);
        SetGain(10000);
    }

    static void Check(int hr, string what)
    {
        if (hr < 0) throw new InvalidOperationException($"g27ffb.dll: {what} falhou (0x{hr:X8})");
    }

    void SetGain(uint gain) => ((delegate* unmanaged<nint, uint, uint, int>)_vt[6])(_drv, 0, gain);
    void Command(uint flags) => ((delegate* unmanaged<nint, uint, uint, int>)_vt[7])(_drv, 0, flags);

    [UnmanagedCallersOnly]
    static void OnPacket(nint packet, nint data)
    {
        VJoyFfb? f;
        lock (typeof(VJoyFfb)) f = _current;
        if (f == null) return;
        try { lock (f._lock) f.Handle(packet); }
        catch (Exception ex) { AppLog.Erro("FFB", "Pacote de FFB do vJoy", ex); }
    }

    void Handle(nint p)
    {
        if (_disposed || Native.Ffb_h_Type(p, out var type) != 0) return;
        _lastPacket = Environment.TickCount64;
        if (_drv == 0)
        {
            try { CreateDriver(_path, _dll); }
            catch (Exception ex)
            {
                _disposed = true; // não tenta de novo a cada pacote; religar o modo tenta outra vez
                AppLog.Erro("FFB", "Não consegui ligar o motor de força para o vJoy", ex);
                return;
            }
            AppLog.Ok("FFB", "O jogo começou a mandar força pelo vJoy: motor da g27ffb.dll ligado no G27");
            foreach (var e in _effects.Values) // o jogo voltou depois de uma pausa longa: recria o que ele já tinha
            {
                Download(e);
                if (e.Playing && e.Handle != 0) ((delegate* unmanaged<nint, uint, uint, uint, uint, int>)_vt[11])(_drv, 0, e.Handle, 0, Infinite);
            }
        }
        if (++_packets == 1) AppLog.Info("FFB", "Primeiro pacote de force feedback do jogo chegou pelo vJoy");
        Native.Ffb_h_EffectBlockIndex(p, out var block);
        if (_seen.Add(type)) AppLog.Info("FFB", $"vJoy: primeiro pacote do tipo 0x{type:X2} (bloco {block})"); // diagnóstico: o que o jogo usa
        switch (type)
        {
            case 0x11: // nova efeito: o driver do vJoy já reservou o índice
                if (Native.Ffb_h_CreateNewEffect(p, out var et, out var id) == 0) Get(id).TypeId = et - 1;
                break;
            case 0x01: // Set Effect Report
            {
                FfbEffReport r;
                if (Native.Ffb_h_Eff_Report(p, &r) != 0) break;
                var e = Get(r.EffectBlockIndex);
                if (r.EffectType is >= 1 and <= 11) e.TypeId = r.EffectType - 1;
                e.Duration = r.Duration == 0xFFFF ? Infinite : r.Duration * 1000u;
                e.StartDelay = r.StartDelay * 1000u;
                e.Gain = r.Gain * 10000u / 255;
                e.Polar = r.Polar != 0;
                if (e.Polar) e.Direction = (int)(r.Direction * 36000L / 0x8000);
                else e.DirX = (short)r.Direction;
                if (_packets < 40) AppLog.Debug("FFB", $"vJoy efeito {r.EffectBlockIndex}: tipo {r.EffectType}, duração {r.Duration}, ganho {r.Gain}, {(e.Polar ? $"polar {e.Direction / 100}°" : $"X {e.DirX}")}");
                Download(e);
                break;
            }
            case 0x02: // envelope
            {
                FfbEnvelope v;
                if (Native.Ffb_h_Eff_Envlp(p, &v) != 0) break;
                var e = Get(v.EffectBlockIndex);
                (e.HasEnv, e.AttackLevel, e.FadeLevel, e.AttackTime, e.FadeTime) = (true, v.AttackLevel, v.FadeLevel, v.AttackTime * 1000, v.FadeTime * 1000);
                Download(e);
                break;
            }
            case 0x03: // condição (mola, amortecedor, inércia, atrito)
            {
                FfbCond c;
                if (Native.Ffb_h_Eff_Cond(p, &c) != 0 || c.IsY != 0) break; // o G27 só tem o eixo X
                var e = Get(c.EffectBlockIndex);
                (e.Offset, e.PosCoef, e.NegCoef, e.PosSat, e.NegSat, e.DeadBand) = (S(c.CenterPointOffset), S(c.PosCoeff), S(c.NegCoeff), c.PosSatur, c.NegSatur, c.DeadBand);
                Download(e);
                break;
            }
            case 0x04: // periódico
            {
                FfbPeriod v;
                if (Native.Ffb_h_Eff_Period(p, &v) != 0) break;
                var e = Get(v.EffectBlockIndex);
                (e.PerMagnitude, e.Offset, e.Phase, e.Period) = (v.Magnitude, S(v.Offset), v.Phase, Math.Max(1, v.Period) * 1000);
                Download(e);
                break;
            }
            case 0x05: // força constante
            {
                FfbConstant v;
                if (Native.Ffb_h_Eff_Constant(p, &v) != 0) break;
                var e = Get(v.EffectBlockIndex);
                e.Magnitude = S(v.Magnitude);
                Download(e);
                break;
            }
            case 0x06: // rampa
            {
                FfbRamp v;
                if (Native.Ffb_h_Eff_Ramp(p, &v) != 0) break;
                var e = Get(v.EffectBlockIndex);
                (e.RampStart, e.RampEnd) = (S(v.Start), S(v.End));
                Download(e);
                break;
            }
            case 0x0A: // start / solo / stop
            {
                FfbOp op;
                if (Native.Ffb_h_EffOp(p, &op) != 0 || !_effects.TryGetValue(op.EffectBlockIndex, out var e) || e.Handle == 0) break;
                e.Playing = op.EffectOp != 3;
                if (op.EffectOp == 3) ((delegate* unmanaged<nint, uint, uint, int>)_vt[12])(_drv, 0, e.Handle);
                else ((delegate* unmanaged<nint, uint, uint, uint, uint, int>)_vt[11])(_drv, 0, e.Handle, op.EffectOp == 2 ? 1u : 0u, op.LoopCount == 0xFF ? Infinite : Math.Max(1u, op.LoopCount));
                break;
            }
            case 0x0B: // bloco liberado
                if (_effects.Remove(block, out var gone) && gone.Handle != 0) ((delegate* unmanaged<nint, uint, uint, int>)_vt[10])(_drv, 0, gone.Handle);
                break;
            case 0x0C: // controle do dispositivo
                if (Native.Ffb_h_DevCtrl(p, out var ctrl) != 0) break;
                Command(ctrl switch { 1 => DisffcOn, 2 => DisffcOff, 3 => DisffcStopAll, 4 => DisffcReset | DisffcOn, 5 => DisffcPause, 6 => DisffcContinue, _ => 0 });
                if (ctrl == 4) _effects.Clear();
                if (ctrl == 3) foreach (var e in _effects.Values) e.Playing = false;
                break;
            case 0x0D: // ganho geral do jogo
                if (Native.Ffb_h_DevGain(p, out var gain) == 0) SetGain(gain * 10000u / 255);
                break;
        }
    }

    /// <summary>O vJoy entrega valores com sinal (-10000..10000) como palavra de 16 bits sem sinal: -5000 chega 60536.</summary>
    public static int S(int v) => v is > 10000 and <= 0xFFFF ? (short)(ushort)v : v;

    Eff Get(uint block) => _effects.TryGetValue(block, out var e) ? e : _effects[block] = new Eff();

    /// <summary>Monta um DIEFFECT (layout x64) com o estado do efeito e chama DownloadEffect.</summary>
    void Download(Eff e)
    {
        if (e.TypeId > 10) return; // tipo ainda não conhecido (ou "custom")
        var b = _buf;
        NativeMemory.Clear(b, 512);
        uint* axes = (uint*)(b + 128);
        int* dir = (int*)(b + 136);
        uint* env = (uint*)(b + 144);
        byte* tsp = b + 192;
        axes[0] = 0; // DIJOFS_X
        dir[0] = e.Polar ? e.Direction : e.DirX;
        uint cb;
        switch (e.TypeId)
        {
            case 0: *(int*)tsp = e.Magnitude; cb = 4; break;
            case 1: ((int*)tsp)[0] = e.RampStart; ((int*)tsp)[1] = e.RampEnd; cb = 8; break;
            case <= 6:
                ((uint*)tsp)[0] = e.PerMagnitude; ((int*)tsp)[1] = e.Offset; ((uint*)tsp)[2] = e.Phase; ((uint*)tsp)[3] = e.Period; cb = 16; break;
            default:
                ((int*)tsp)[0] = e.Offset; ((int*)tsp)[1] = e.PosCoef; ((int*)tsp)[2] = e.NegCoef;
                ((uint*)tsp)[3] = e.PosSat; ((uint*)tsp)[4] = e.NegSat; ((int*)tsp)[5] = e.DeadBand; cb = 24; break;
        }
        if (e.HasEnv)
        {
            env[0] = 20; env[1] = e.AttackLevel; env[2] = e.AttackTime; env[3] = e.FadeLevel; env[4] = e.FadeTime;
        }
        *(uint*)(b + 0) = 80;                                    // dwSize = sizeof(DIEFFECT) no x64
        *(uint*)(b + 4) = DieffObjectOffsets | (e.Polar ? DieffPolar : DieffCartesian);
        *(uint*)(b + 8) = e.Duration;
        *(uint*)(b + 16) = e.Gain;
        *(uint*)(b + 20) = Infinite;                             // sem botão de disparo
        *(uint*)(b + 28) = 1;                                    // cAxes
        *(nint*)(b + 32) = (nint)axes;
        *(nint*)(b + 40) = (nint)dir;
        *(nint*)(b + 48) = e.HasEnv ? (nint)env : 0;
        *(uint*)(b + 56) = cb;
        *(nint*)(b + 64) = (nint)tsp;
        *(uint*)(b + 72) = e.StartDelay;
        uint handle = e.Handle;
        int hr = ((delegate* unmanaged<nint, uint, uint, uint*, void*, uint, int>)_vt[9])(_drv, 0, e.TypeId, &handle, b, 0);
        if (hr < 0) { AppLog.Aviso("FFB", $"Efeito {TypeNames[e.TypeId]} do jogo recusado pela DLL (0x{hr:X8})"); return; }
        if (e.Handle == 0)
            AppLog.Info("FFB", $"Efeito do jogo pelo vJoy: {TypeNames[e.TypeId]}, ganho {e.Gain / 100}%, duração {(e.Duration == Infinite ? "infinita" : $"{e.Duration / 1000} ms")}, " +
                               (e.Polar ? $"direção {e.Direction / 100}°" : $"X {e.DirX}"));
        e.Handle = handle;
    }

    public void Dispose()
    {
        lock (typeof(VJoyFfb)) if (_current == this) _current = null;
        _idle?.Dispose();
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            EndDriver();
            NativeMemory.Free(_buf);
        }
        AppLog.Info("FFB", "Force feedback do vJoy desligado");
    }

    void CheckIdle()
    {
        lock (_lock)
            if (!_disposed && _drv != 0 && Environment.TickCount64 - _lastPacket > IdleMs)
            {
                EndDriver();
                AppLog.Info("FFB", $"Sem força do jogo há {IdleMs / 1000} s: motor parado, mola do volante de volta");
            }
    }

    /// <summary>DeviceID "fim": a DLL para o mixer e devolve a mola do volante (ExitAutocenter).</summary>
    void EndDriver()
    {
        if (_drv == 0) return;
        ((delegate* unmanaged<nint, uint, uint, uint, uint, void*, int>)_vt[3])(_drv, 0x0800, 0, 0, 0, null);
        ((delegate* unmanaged<nint, uint>)_vt[2])(_drv); // Release
        _drv = 0;
    }

    // ===== estruturas do vjoyinterface.h (alinhamento padrão do MSVC) =====
    [StructLayout(LayoutKind.Sequential)] struct FfbEffReport { public byte EffectBlockIndex; public uint EffectType; public ushort Duration, TrigerRpt, SamplePrd, StartDelay; public byte Gain, TrigerBtn, AxesEnabledDirection; public int Polar; public ushort Direction, DirY; }
    [StructLayout(LayoutKind.Sequential)] struct FfbConstant { public byte EffectBlockIndex; public int Magnitude; }
    [StructLayout(LayoutKind.Sequential)] struct FfbRamp { public byte EffectBlockIndex; public int Start, End; }
    [StructLayout(LayoutKind.Sequential)] struct FfbOp { public byte EffectBlockIndex; public uint EffectOp; public byte LoopCount; }
    [StructLayout(LayoutKind.Sequential)] struct FfbPeriod { public byte EffectBlockIndex; public uint Magnitude; public int Offset; public uint Phase, Period; }
    [StructLayout(LayoutKind.Sequential)] struct FfbCond { public byte EffectBlockIndex; public int IsY, CenterPointOffset, PosCoeff, NegCoeff; public uint PosSatur, NegSatur; public int DeadBand; }
    [StructLayout(LayoutKind.Sequential)] struct FfbEnvelope { public byte EffectBlockIndex; public uint AttackLevel, FadeLevel, AttackTime, FadeTime; }

    static class Native
    {
        const string D = "vJoyInterface.dll";
        [DllImport(D)] public static extern void FfbRegisterGenCB(delegate* unmanaged<nint, nint, void> cb, nint data);
        [DllImport(D)] public static extern uint Ffb_h_Type(nint p, out uint type);
        [DllImport(D)] public static extern uint Ffb_h_EffectBlockIndex(nint p, out uint index);
        [DllImport(D)] public static extern uint Ffb_h_CreateNewEffect(nint p, out uint type, out uint id);
        [DllImport(D)] public static extern uint Ffb_h_Eff_Report(nint p, FfbEffReport* e);
        [DllImport(D)] public static extern uint Ffb_h_Eff_Envlp(nint p, FfbEnvelope* e);
        [DllImport(D)] public static extern uint Ffb_h_Eff_Cond(nint p, FfbCond* e);
        [DllImport(D)] public static extern uint Ffb_h_Eff_Period(nint p, FfbPeriod* e);
        [DllImport(D)] public static extern uint Ffb_h_Eff_Constant(nint p, FfbConstant* e);
        [DllImport(D)] public static extern uint Ffb_h_Eff_Ramp(nint p, FfbRamp* e);
        [DllImport(D)] public static extern uint Ffb_h_EffOp(nint p, FfbOp* e);
        [DllImport(D)] public static extern uint Ffb_h_DevCtrl(nint p, out uint ctrl);
        [DllImport(D)] public static extern uint Ffb_h_DevGain(nint p, out byte gain);
    }
}
