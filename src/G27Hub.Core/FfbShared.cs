using System.IO.MemoryMappedFiles;

namespace G27Hub.Core;

/// <summary>Força que a g27ffb.dll está mandando agora (memória "Local\G27Hub_Shared", layout em hub.c).</summary>
public sealed record FfbShared(int Pid, int Force, int Clips, int AgeMs)
{
    public static FfbShared? Read()
    {
        try
        {
            using var map = MemoryMappedFile.OpenExisting(@"Local\G27Hub_Shared", MemoryMappedFileRights.Read);
            using var v = map.CreateViewAccessor(0, 24, MemoryMappedFileAccess.Read);
            if (v.ReadUInt32(0) != 0x48373247) return null;
            return new(v.ReadInt32(8), v.ReadInt32(12), v.ReadInt32(16), Environment.TickCount - v.ReadInt32(20));
        }
        catch (FileNotFoundException) { return null; } // nenhum jogo com a DLL carregada
    }
}
