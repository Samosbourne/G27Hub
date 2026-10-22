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

        case "spin" when a.Length > 1 && double.TryParse(a[1], out var target):
            return WithDevice(d => Spin(d, target, a.Skip(2).Select(double.Parse).ToArray()));

        case "ffb" when a.Length > 1:
            return Ffb(a[1..]);

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

                  ffb register|unregister registra/remove a g27ffb.dll (x64 + x86) como driver de FFB (HKCU)
                  ffb status              registro, configuração e força atual vinda do jogo
                  ffb pulse               dois pulsos curtos direto no HID (sem DirectInput)
                  ffb test [x64|x86] [const|sine|spring|damper|all]
                                          testa via DirectInput, como um jogo (x86 = jogo 32 bits)
                  ffb gain <0-100>        ganho geral do FFB
                  ffb invert on|off       inverte o sentido da força
                  ffb log 0|1|2           log da DLL: desligado / eventos / detalhado

                  -v                      modo detalhado (mostra os bytes enviados)
                """);
            return a.Length == 0 ? 0 : 2;
    }
}

/// <summary>Leva o volante a um ângulo com o WheelSpinner por 3 s e mostra o caminho (ajuste: spin &lt;graus&gt; [kp kd max min]).</summary>
static void Spin(G27Device d, double target, double[] gains)
{
    var s = new WheelSpinner();
    if (gains.Length > 0) s.Kp = gains[0];
    if (gains.Length > 1) s.Kd = gains[1];
    if (gains.Length > 2) s.MaxForce = gains[2];
    if (gains.Length > 3) s.MinPush = gains[3];
    d.SetAutocenter(0);
    Thread.Sleep(100);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    double last = 0, maxOver = 0, start = d.State?.Angle ?? 0;
    int lastForce = int.MinValue;
    long nextPrint = 0;
    while (sw.ElapsedMilliseconds < 3000)
    {
        var t = sw.Elapsed.TotalSeconds;
        var angle = d.State?.Angle ?? 0;
        var f = s.Step(angle, target, t - last);
        last = t;
        if (f != lastForce) { d.SetForce(f); lastForce = f; }
        if (Math.Sign(target - start) != Math.Sign(target - angle)) maxOver = Math.Max(maxOver, Math.Abs(angle - target));
        if (sw.ElapsedMilliseconds >= nextPrint) { Console.WriteLine($"{sw.ElapsedMilliseconds,5} ms  ângulo {angle,7:0.0}°  força {f,4}%"); nextPrint += 100; }
        Thread.Sleep(1);
    }
    d.SetForce(0);
    Console.WriteLine($"de {start:0.0}° até {target:0.0}°: final {d.State?.Angle:0.0}°, passou do alvo {maxOver:0.0}°");
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

static int Ffb(string[] a)
{
    switch (a[0])
    {
        case "register":
            return FfbRegistration.Register() ? 0 : 1;

        case "unregister":
            FfbRegistration.Unregister();
            return 0;

        case "status":
            foreach (var line in FfbRegistration.Status()) Console.WriteLine(line);
            var s = FfbShared.Read();
            Console.WriteLine(s == null
                ? "Força: nenhum jogo com a DLL carregada"
                : $"Força: {s.Force / 100.0:+0.0;-0.0}% (pid {s.Pid}, há {s.AgeMs} ms, {s.Clips} ticks saturados)");
            return 0;

        case "pulse":
            return WithDevice(d =>
            {
                Thread.Sleep(200);
                foreach (var pct in new[] { 30, -30 })
                {
                    var before = d.State?.Angle;
                    d.SetForce(pct);
                    Thread.Sleep(300);
                    d.SetForce(0);
                    Thread.Sleep(400);
                    AppLog.Info("FFB", $"Pulso {pct:+0;-0}%: volante {before:+0.0;-0.0}° → {d.State?.Angle:+0.0;-0.0}°");
                }
            });

        case "test":
            var arch = a.Skip(1).FirstOrDefault(x => x is "x64" or "x86") ?? "x64";
            var mode = a.Skip(1).FirstOrDefault(x => x is not ("x64" or "x86")) ?? "const";
            var exe = Path.Combine(AppContext.BaseDirectory, arch, "g27fftest.exe");
            if (!File.Exists(exe))
            {
                AppLog.Erro("FFB", $"{exe} não existe (rode src/native/build.ps1 e recompile)");
                return 1;
            }
            FfbLogServer.Start();
            AppLog.Info("FFB", $"Teste DirectInput {arch} '{mode}'");
            using (var p = System.Diagnostics.Process.Start(exe, mode))
                p.WaitForExit();
            Thread.Sleep(500); // últimas linhas do pipe
            return 0;

        case "gain" when a.Length > 1 && int.TryParse(a[1], out var gain) && gain is >= 0 and <= 100:
            FfbRegistration.SetConfig("MasterGain", gain);
            return 0;

        case "invert" when a.Length > 1 && a[1] is "on" or "off":
            FfbRegistration.SetConfig("Invert", a[1] == "on" ? 1 : 0);
            return 0;

        case "log" when a.Length > 1 && a[1] is "0" or "1" or "2":
            FfbRegistration.SetConfig("Log", int.Parse(a[1]));
            return 0;

        default:
            return Run([]);
    }
}

static int Monitor()
{
    AppLog.Memory.Added -= Print; // os logs aparecem dentro do painel
    FfbLogServer.Start();
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
