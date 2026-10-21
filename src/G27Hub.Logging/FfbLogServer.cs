using System.IO.Pipes;

namespace G27Hub.Logging;

/// <summary>
/// Recebe os logs da g27ffb.dll (que roda dentro dos jogos) pelo pipe \\.\pipe\G27Hub_Log.
/// Linhas "NIVEL|mensagem"; um cliente por processo de jogo.
/// </summary>
public static class FfbLogServer
{
    public const string PipeName = "G27Hub_Log";

    public static void Start(CancellationToken ct = default) => _ = Task.Run(() => AcceptLoop(ct), ct);

    static async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(PipeName, PipeDirection.In,
                NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try
            {
                await server.WaitForConnectionAsync(ct);
            }
            catch (Exception ex)
            {
                server.Dispose();
                if (ex is not OperationCanceledException) AppLog.Erro("FFB", "Servidor do pipe de log parou", ex);
                return;
            }
            _ = Task.Run(() => ReadClient(server), ct);
        }
    }

    static void ReadClient(NamedPipeServerStream pipe)
    {
        using (pipe)
        using (var reader = new StreamReader(pipe))
        {
            try
            {
                while (reader.ReadLine() is { } line)
                {
                    var bar = line.IndexOf('|');
                    if (bar > 0) AppLog.Write(line[..bar], "FFB", line[(bar + 1)..]);
                }
            }
            catch (IOException) { } // jogo fechou
        }
    }
}
