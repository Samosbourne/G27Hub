using System.IO.MemoryMappedFiles;

namespace G27Hub.Core;

/// <summary>Força que a g27ffb.dll está mandando agora (memória "Local\G27Hub_Shared", layout em hub.c).</summary>
public sealed record FfbShared(int Pid, int Force, int Clips, int AgeMs)
{
    static MemoryMappedViewAccessor? _view;
    static long _nextTry;

    /// <summary>Barato o bastante para chamar a cada quadro: sem jogo, só tenta abrir de novo 1x por segundo.</summary>
    public static FfbShared? Read()
    {
        if (_view == null)
        {
            if (Environment.TickCount64 < _nextTry) return null;
            try
            {
                // ponytail: o mapa fica aberto até o app fechar; mantém a memória viva entre um jogo e outro
                _view = MemoryMappedFile.OpenExisting(@"Local\G27Hub_Shared", MemoryMappedFileRights.Read)
                    .CreateViewAccessor(0, 24, MemoryMappedFileAccess.Read);
            }
            catch (FileNotFoundException)
            {
                _nextTry = Environment.TickCount64 + 1000; // nenhum jogo com a DLL carregada
                return null;
            }
        }
        if (_view.ReadUInt32(0) != 0x48373247) return null;
        return new(_view.ReadInt32(8), _view.ReadInt32(12), _view.ReadInt32(16), Environment.TickCount - _view.ReadInt32(20));
    }
}
