using System.IO.MemoryMappedFiles;

namespace G27Hub.Core;

/// <summary>
/// Memória "Local\G27Hub_Shared" (layout em hub.c): a g27ffb.dll publica a força que está mandando, e o app escreve
/// a vibração extra (Fase 10) que a DLL soma à força do jogo.
/// </summary>
public sealed record FfbShared(int Pid, int Force, int Clips, int AgeMs)
{
    const int Size = 64;
    static MemoryMappedViewAccessor? _view;
    static long _nextTry;

    // ponytail: o mapa fica aberto até o app fechar; mantém a memória viva entre um jogo e outro
    static MemoryMappedViewAccessor? View()
    {
        if (_view != null || Environment.TickCount64 < _nextTry) return _view;
        try { _view = MemoryMappedFile.CreateOrOpen(@"Local\G27Hub_Shared", Size).CreateViewAccessor(0, Size); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _nextTry = Environment.TickCount64 + 1000; }
        return _view;
    }

    /// <summary>Força atual do jogo, ou null se nenhuma DLL publicou ainda. Barato o bastante para chamar a cada quadro.</summary>
    public static FfbShared? Read()
    {
        var v = View();
        if (v == null || v.ReadUInt32(0) != 0x48373247) return null; // a DLL grava "G27H" ao carregar
        return new(v.ReadInt32(8), v.ReadInt32(12), v.ReadInt32(16), Environment.TickCount - v.ReadInt32(20));
    }

    /// <summary>Vibração extra para a DLL somar. Sem chamadas por 250 ms, a DLL para de vibrar sozinha.</summary>
    public static void WriteVibration(in Vibration vib)
    {
        if (View() is not { } v) return;
        v.Write(28, vib.EngineAmp);
        v.Write(32, (int)(vib.EngineHz * 100));
        v.Write(36, vib.RoadAmp);
        v.Write(40, (int)(vib.RoadHz * 100));
        if (vib.KickMs > 0) // tranco novo; sem tranco, o anterior termina no tempo dele
        {
            v.Write(44, vib.KickAmp);
            v.Write(48, Environment.TickCount + vib.KickMs);
        }
        v.Write(24, Environment.TickCount); // por último: a DLL só usa o resto se este estiver em dia
    }
}
