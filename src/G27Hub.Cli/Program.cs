using G27Hub.Core;
using G27Hub.Logging;

AppLog.Init(verbose: args.Contains("-v"));
args = args.Where(a => a != "-v").ToArray();
AppLog.Memory.Added += Print;

try { return Run(args); }
catch (Exception ex) { AppLog.Erro("CLI", ex.Message, ex); return 1; }
finally { AppLog.Close(); }

static int Run(string[] a)
{
    switch (a.FirstOrDefault())
    {
        case "list":
            var found = G27Device.Find().ToList();
            if (found.Count == 0) Console.WriteLine("Nenhum G27 encontrado (procurando 046D:C294 e 046D:C29B).");
            foreach (var d in found)
                Console.WriteLine($"046D:{d.ProductID:X4}  {(d.ProductID == G27Commands.PidNative ? "nativo" : "compatível (DFEX)")}  {d.GetFriendlyName()}  {d.DevicePath}");
            return 0;

        case "native":
            return WithDevice(_ => { });

        case "range" when a.Length > 1 && int.TryParse(a[1], out var deg):
            return WithDevice(d => d.SetRange(deg));

        case "autocenter" when a.Length > 1 && (a[1] == "off" || int.TryParse(a[1], out _)):
            return WithDevice(d => d.SetAutocenter(a[1] == "off" ? 0 : int.Parse(a[1])));

        case "leds" when a.Length > 1 && a[1] == "teste":
            return WithDevice(d =>
            {
                for (int bit = 0; bit < 5; bit++)
                {
                    Console.WriteLine($"Bit {bit} aceso — anote qual LED acendeu");
                    d.SetLeds(1 << bit);
                    Thread.Sleep(2000);
                }
                d.SetLeds(0x1F);
                Thread.Sleep(1000);
                d.SetLeds(0);
            });

        case "leds" when a.Length > 1 && int.TryParse(a[1], out var mask) && mask is >= 0 and <= 31:
            return WithDevice(d => d.SetLeds(mask));

        case "monitor":
            return Monitor();

        case "log":
            Console.WriteLine($"Pasta: {AppLog.LogDir}");
            var file = new DirectoryInfo(AppLog.LogDir).GetFiles("g27hub-*.log").MaxBy(f => f.LastWriteTime);
            if (file == null) return 0;
            Console.WriteLine($"Arquivo: {file.FullName}\n");
            using (var r = new StreamReader(new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                foreach (var line in r.ReadToEnd().Split('\n').TakeLast(41)) Console.WriteLine(line.TrimEnd());
            return 0;

        default:
            Console.WriteLine("""
                g27cli <comando> [-v]

                  list                    lista G27 conectados
                  native                  coloca o G27 em modo nativo (900°, pedais separados, embreagem)
                  range <40-900>          modo nativo + rotação em graus
                  autocenter off|<0-100>  mola de autocentro
                  leds <0-31>|teste       LEDs de RPM (máscara de 5 bits) ou teste bit a bit
                  monitor                 entradas ao vivo, reconecta sozinho (Ctrl+C sai)
                  log                     caminho e fim do arquivo de log

                  -v                      modo detalhado (mostra os bytes enviados)
                """);
            return a.Length == 0 ? 0 : 2;
    }
}

static int WithDevice(Action<G27Device> action)
{
    using var dev = new G27Device();
    if (!dev.Connect())
    {
        AppLog.Erro("HID", "G27 não encontrado ou não conectou (veja 'g27cli list')");
        return 1;
    }
    action(dev);
    return 0;
}

static int Monitor()
{
    AppLog.Memory.Added -= Print; // os logs aparecem dentro do painel
    using var dev = new G27Device();
    using var exit = new ManualResetEvent(false);
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; exit.Set(); };
    dev.Start();

    Console.Clear();
    Console.CursorVisible = false;
    while (!exit.WaitOne(50))
    {
        Console.SetCursorPosition(0, 0);
        var s = dev.State;
        var width = Math.Max(Console.WindowWidth - 1, 40);
        string[] lines =
        [
            dev.IsConnected ? $"G27 046D:{dev.Device!.ProductID:X4}  rotação {dev.Range}°   (Ctrl+C sai)" : "Aguardando o G27...   (Ctrl+C sai)",
            "",
            s?.Angle is { } ang ? $"Volante  {ang,7:+0.0;-0.0}°   bruto {s.Axes["X"].Value}/{s.Axes["X"].Max}" : "Volante  -",
            "Eixos    " + (s == null ? "-" : string.Join("  ", s.Axes.Where(x => x.Key != "X").Select(x => $"{x.Key}={x.Value.Value}"))),
            "Botões   " + (s == null || s.Buttons.Count == 0 ? "-" : string.Join(" ", s.Buttons)),
            "Hat      " + (s == null || s.Hat < 0 ? "-" : s.Hat.ToString()),
            "",
            "Log:",
            .. AppLog.Memory.Snapshot().TakeLast(8).Select(l => "  " + l),
        ];
        foreach (var line in lines) Console.WriteLine(line.Length > width ? line[..width] : line.PadRight(width));
    }
    Console.CursorVisible = true;
    return 0;
}

static void Print(LogLine l)
{
    Console.ForegroundColor = l.Nivel switch
    {
        "OK" => ConsoleColor.Green,
        "AVISO" => ConsoleColor.Yellow,
        "ERRO" => ConsoleColor.Red,
        _ => ConsoleColor.Gray,
    };
    Console.WriteLine(l);
    Console.ResetColor();
}
