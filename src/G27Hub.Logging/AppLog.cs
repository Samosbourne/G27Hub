using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace G27Hub.Logging;

public sealed record LogLine(DateTimeOffset Hora, string Nivel, string Origem, string Mensagem)
{
    public override string ToString() => $"{Hora:HH:mm:ss}  {Nivel,-5}  {Origem,-10} {Mensagem}";
}

/// <summary>Log central: arquivo rotativo + buffer em memória para o console de diagnóstico.</summary>
public static class AppLog
{
    public static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "G27Hub", "logs");

    public static readonly MemorySink Memory = new(5000);

    const string Template = "{Timestamp:HH:mm:ss}  {Nivel,-5}  {Origem,-10} {Message:lj}{NewLine}{Exception}";

    public static void Init(bool verbose = false)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(verbose ? LogEventLevel.Debug : LogEventLevel.Information)
            .WriteTo.File(Path.Combine(LogDir, "g27hub-.log"), rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14, shared: true, outputTemplate: Template)
            .WriteTo.Sink(Memory)
            .CreateLogger();
    }

    public static void Close() => Log.CloseAndFlush();

    public static void Debug(string origem, string msg) => Write(LogEventLevel.Debug, "DEBUG", origem, msg);
    public static void Info(string origem, string msg) => Write(LogEventLevel.Information, "INFO", origem, msg);
    public static void Ok(string origem, string msg) => Write(LogEventLevel.Information, "OK", origem, msg);
    public static void Aviso(string origem, string msg) => Write(LogEventLevel.Warning, "AVISO", origem, msg);
    public static void Erro(string origem, string msg, Exception? ex = null) => Write(LogEventLevel.Error, "ERRO", origem, msg, ex);

    static void Write(LogEventLevel level, string nivel, string origem, string msg, Exception? ex = null) =>
        Log.ForContext("Nivel", nivel).ForContext("Origem", origem).Write(level, ex, "{Msg:l}", msg);
}

/// <summary>Guarda as últimas N linhas e avisa quem estiver ouvindo (console do app, g27cli).</summary>
public sealed class MemorySink(int capacity) : ILogEventSink
{
    readonly Queue<LogLine> _lines = new();

    public event Action<LogLine>? Added;

    public void Emit(LogEvent e)
    {
        var line = new LogLine(e.Timestamp, Prop(e, "Nivel"), Prop(e, "Origem"),
            e.RenderMessage() + (e.Exception is null ? "" : $" ({e.Exception.Message})"));
        lock (_lines)
        {
            _lines.Enqueue(line);
            if (_lines.Count > capacity) _lines.Dequeue();
        }
        Added?.Invoke(line);
    }

    public LogLine[] Snapshot()
    {
        lock (_lines) return _lines.ToArray();
    }

    static string Prop(LogEvent e, string name) =>
        e.Properties.TryGetValue(name, out var v) && v is ScalarValue { Value: string s } ? s : "";
}
