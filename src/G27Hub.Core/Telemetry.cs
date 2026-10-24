using System.IO.MemoryMappedFiles;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using G27Hub.Logging;
using static System.Buffers.Binary.BinaryPrimitives;

namespace G27Hub.Core;

/// <summary>
/// Um instante da telemetria do jogo. Gear: -1 ré, 0 neutro, 1… marchas. Fuel 0–1, Speed km/h, Abs 0–1 (ABS agindo),
/// Accel m/s² em (lateral, vertical, longitudinal) do carro (null = jogo não manda).
/// </summary>
public sealed record TelemetryFrame(string Source, float Rpm, float MaxRpm, int? Gear,
    bool ShiftLight = false, bool PitLimiter = false, float? Fuel = null, float? GamePercent = null, float? Speed = null,
    float? Abs = null, Vector3? Accel = null);

/// <summary>
/// Leitores de telemetria dos jogos (Fase 7). Tudo em 127.0.0.1, sem admin e sem firewall:
/// UDP do BeamNG (mod do G27 Hub, 4445), OutGauge (LFS/BeamNG, 4444), Forza Data Out (5300), F1 (20777);
/// memória compartilhada do Assetto Corsa / ACC.
/// </summary>
public sealed class TelemetryHub : IDisposable
{
    public const int ModPort = 4445, OutGaugePort = 4444, ForzaPort = 5300, F1Port = 20777;
    const int StaleMs = 1000;

    readonly CancellationTokenSource _cts = new();
    readonly List<UdpClient> _sockets = [];
    TelemetryFrame? _last;
    long _lastMs = -StaleMs;
    int _lastPriority;
    string? _source;

    /// <summary>Último quadro, ou null se nenhum jogo mandou nada no último segundo.</summary>
    public TelemetryFrame? Current => Environment.TickCount64 - _lastMs < StaleMs ? _last : null;

    public void Start()
    {
        float outGaugeMax = 0;
        string outGaugeCar = "";
        Listen(ModPort, b => HubPacket(b), priority: 1); // o mod tem a RPM máxima de verdade: vence o OutGauge do mesmo jogo
        Listen(OutGaugePort, b => OutGauge(b, ref outGaugeMax, ref outGaugeCar));
        Listen(ForzaPort, Forza);
        Listen(F1Port, F1);
        new Thread(PollAssettoCorsa) { IsBackground = true, Name = "Telemetria AC" }.Start();
        new Thread(WatchStale) { IsBackground = true, Name = "Telemetria" }.Start();
    }

    delegate TelemetryFrame? Parser(byte[] b);

    void Listen(int port, Parser parse, int priority = 0)
    {
        UdpClient udp;
        try { udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port)); }
        catch (SocketException ex)
        {
            AppLog.Aviso("TELEMETRIA", $"Porta UDP {port} ocupada (SimHub ou outro app?); essa fonte fica desligada ({ex.SocketErrorCode})");
            return;
        }
        _sockets.Add(udp);
        new Thread(() =>
        {
            var from = new IPEndPoint(IPAddress.Any, 0);
            while (!_cts.IsCancellationRequested)
            {
                try { if (parse(udp.Receive(ref from)) is { } f) Push(f, priority); }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset) { } // ICMP de envio anterior; ignora
                catch (Exception) when (_cts.IsCancellationRequested) { }
                catch (Exception ex) { AppLog.Debug("TELEMETRIA", $"Pacote inválido na porta {port}: {ex.Message}"); }
            }
        }) { IsBackground = true, Name = $"Telemetria UDP {port}" }.Start();
    }

    void Push(TelemetryFrame f, int priority = 0)
    {
        var now = Environment.TickCount64;
        if (priority < _lastPriority && now - _lastMs < StaleMs) return;
        _last = f;
        _lastPriority = priority;
        _lastMs = now;
        if (_source != f.Source)
        {
            _source = f.Source;
            AppLog.Ok("TELEMETRIA", $"Recebendo telemetria: {f.Source}");
        }
    }

    void WatchStale()
    {
        while (!_cts.Token.WaitHandle.WaitOne(500))
            if (_source != null && Current == null)
            {
                AppLog.Info("TELEMETRIA", $"{_source} parou de mandar telemetria");
                _source = null;
                _lastPriority = 0;
            }
    }

    // ================= formatos (públicos para os testes) =================

    /// <summary>
    /// Pacote do mod do BeamNG ("G27H", <see cref="BeamNgTelemetry"/>) ou do plugin do ETS2/ATS ("G27T", <see cref="ScsTelemetry"/>).
    /// 28 bytes (Fase 7) ou 48 com velocidade (m/s), ABS e aceleração (Fase 10).
    /// </summary>
    public static TelemetryFrame? HubPacket(ReadOnlySpan<byte> b)
    {
        if (b.Length < 28 || b[0] != 'G' || b[1] != '2' || b[2] != '7' || b[3] is not ((byte)'H' or (byte)'T')) return null;
        bool full = b.Length >= 48;
        return new(b[3] == 'H' ? "BeamNG.drive" : "ETS2 / ATS", F(b, 4), F(b, 8), ReadInt32LittleEndian(b[16..]),
            ShiftLight: ReadInt32LittleEndian(b[20..]) != 0, Fuel: F(b, 24),
            Speed: full ? F(b, 28) * 3.6f : null, Abs: full ? F(b, 32) : null, Accel: full ? new(F(b, 36), F(b, 40), F(b, 44)) : null);
    }

    /// <summary>OutGauge do Live for Speed (o BeamNG também manda). Não tem RPM máxima: aprende a maior vista por carro.</summary>
    public static TelemetryFrame? OutGauge(ReadOnlySpan<byte> b, ref float maxSeen, ref string car)
    {
        if (b.Length < 92) return null;
        var name = System.Text.Encoding.ASCII.GetString(b[4..8]).TrimEnd('\0');
        if (name != car) { car = name; maxSeen = 0; }
        float rpm = F(b, 16);
        maxSeen = Math.Max(maxSeen, rpm);
        uint lights = ReadUInt32LittleEndian(b[44..]);
        return new(name == "beam" ? "BeamNG.drive (OutGauge)" : "OutGauge (LFS)", rpm, maxSeen, b[10] - 1,
            ShiftLight: (lights & 1) != 0, PitLimiter: (lights & 8) != 0, Fuel: F(b, 28), Speed: F(b, 12) * 3.6f);
    }

    /// <summary>Forza "Data Out" (Sled ou Dash, Motorsport e Horizon). IsRaceOn = 0 nos menus.</summary>
    public static TelemetryFrame? Forza(byte[] b)
    {
        if (b.Length < 232 || ReadInt32LittleEndian(b) == 0) return null;
        // a marcha muda de posição entre as versões; os LEDs não precisam. Aceleração: X direita, Y cima, Z frente
        return new("Forza", F(b, 16), F(b, 8), null, Accel: new(F(b, 20), F(b, 24), F(b, 28)));
    }

    /// <summary>F1 22–25 (EA/Codemasters): pacote Car Telemetry (id 6) do carro do jogador; usa a barra de giro do jogo.</summary>
    public static TelemetryFrame? F1(byte[] b)
    {
        if (b.Length < 24) return null;
        int format = ReadUInt16LittleEndian(b);
        (int header, int id, int player) = format >= 2023 ? (29, b[6], b[27]) : (24, b[5], b[22]);
        int car = header + player * 60;
        if (id != 6 || b.Length < car + 60) return null;
        var gear = (sbyte)b[car + 15];
        return new($"F1 {(format >= 2023 ? format : 2022)}", ReadUInt16LittleEndian(b.AsSpan(car + 16)), 0, gear,
            GamePercent: b[car + 19] / 100f, Speed: ReadUInt16LittleEndian(b.AsSpan(car)));
    }

    static float F(ReadOnlySpan<byte> b, int at) => ReadSingleLittleEndian(b[at..]);

    // ================= Assetto Corsa / ACC (memória compartilhada) =================

    void PollAssettoCorsa()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var physics = MemoryMappedFile.OpenExisting("Local\\acpmf_physics", MemoryMappedFileRights.Read);
                using var stat = MemoryMappedFile.OpenExisting("Local\\acpmf_static", MemoryMappedFileRights.Read);
                using var p = physics.CreateViewAccessor(0, 256, MemoryMappedFileAccess.Read);
                using var s = stat.CreateViewAccessor(0, 420, MemoryMappedFileAccess.Read);
                int lastPacket = -1;
                long lastChange = 0;
                while (!_cts.Token.WaitHandle.WaitOne(15))
                {
                    int packet = p.ReadInt32(0);
                    if (packet != lastPacket) { lastPacket = packet; lastChange = Environment.TickCount64; }
                    if (Environment.TickCount64 - lastChange > StaleMs) // pausado, nos menus ou fechado
                    {
                        if (Environment.TickCount64 - lastChange > 10_000) break; // reabre (o jogo pode ter sido reiniciado)
                        continue;
                    }
                    float maxFuel = s.ReadSingle(416);
                    Push(new("Assetto Corsa / ACC", p.ReadInt32(20), s.ReadInt32(412), p.ReadInt32(16) - 1,
                        PitLimiter: p.ReadInt32(248) != 0, Fuel: maxFuel > 0 ? p.ReadSingle(12) / maxFuel : null, Speed: p.ReadSingle(28),
                        Abs: Math.Clamp(p.ReadSingle(252), 0, 1), // accG em g: x lateral, y vertical, z longitudinal
                        Accel: new Vector3(p.ReadSingle(44), p.ReadSingle(48), p.ReadSingle(52)) * 9.81f));
                }
            }
            catch (FileNotFoundException) { } // jogo fechado
            catch (Exception ex) { AppLog.Debug("TELEMETRIA", $"Assetto Corsa: {ex.Message}"); }
            _cts.Token.WaitHandle.WaitOne(2000);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        foreach (var s in _sockets) s.Dispose();
    }
}

/// <summary>
/// BeamNG.drive: o OutGauge do jogo não manda a RPM máxima do carro. Este mod (um protocolo UDP do próprio BeamNG,
/// em mods/unpacked/g27hub) manda RPM, RPM máxima, marcha, luz de troca e combustível para 127.0.0.1:4445.
/// Os "outros protocolos" vêm ligados de fábrica no BeamNG. Desfazer = apagar a pasta.
/// </summary>
public static class BeamNgTelemetry
{
    public static string ModDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BeamNG", "BeamNG.drive", "current", "mods", "unpacked", "g27hub");
    static string LuaPath(string dir) => Path.Combine(dir, "lua", "vehicle", "protocols", "g27hub.lua");

    public static bool IsInstalled(string? dir = null) => File.Exists(LuaPath(dir ?? ModDir));

    public static void Install(string? dir = null)
    {
        var path = LuaPath(dir ?? ModDir);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Lua);
        AppLog.Ok("TELEMETRIA", $"Mod de telemetria do BeamNG instalado em {dir ?? ModDir} (vale a partir do próximo carro carregado)");
    }

    /// <summary>Mod instalado numa versão antiga (sem os campos da Fase 10): reescreve. Vale no próximo carro carregado.</summary>
    public static void UpdateIfOld(string? dir = null)
    {
        var path = LuaPath(dir ?? ModDir);
        if (!File.Exists(path) || File.ReadAllText(path) == Lua) return;
        File.WriteAllText(path, Lua);
        AppLog.Ok("TELEMETRIA", "Mod de telemetria do BeamNG atualizado (velocidade, ABS e aceleração para a vibração extra)");
    }

    public static void Uninstall(string? dir = null)
    {
        dir ??= ModDir;
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        AppLog.Info("TELEMETRIA", "Mod de telemetria do BeamNG removido");
    }

    static readonly string Lua = $$"""
        -- G27 Hub: RPM, RPM máxima, marcha e luz de troca para os LEDs do Logitech G27, e velocidade, ABS e aceleração
        -- para a vibração extra no force feedback (UDP 127.0.0.1:{{TelemetryHub.ModPort}}).
        -- Instalado pelo G27 Hub; para remover, apague a pasta mods/unpacked/g27hub.
        local M = {}

        local function init() end
        local function reset() end
        local function getAddress() return "127.0.0.1" end
        local function getPort() return {{TelemetryHub.ModPort}} end
        local function getMaxUpdateRate() return 60 end
        local function isPhysicsStepUsed() return false end

        local function getStructDefinition()
          return [[
            char  magic[4];
            float rpm;
            float maxRpm;
            float idleRpm;
            int   gear;
            int   shiftLight;
            float fuel;
            float speed;
            float abs;
            float accLat;
            float accVert;
            float accLong;
          ]]
        end

        local function fillStruct(o, dtSim)
          local e = electrics.values
          if not e.rpm then return end
          o.magic = "G27H"
          o.rpm = e.rpm or 0
          o.maxRpm = e.maxrpm or 0
          o.idleRpm = e.idlerpm or 0
          o.gear = e.gearIndex or 0
          o.shiftLight = e.shouldShift and 1 or 0
          o.fuel = e.fuel or 0
          o.speed = e.wheelspeed or e.airspeed or 0
          o.abs = (e.absActive or 0) ~= 0 and 1 or 0
          local s = sensors.ffiSensors
          o.accLat, o.accVert, o.accLong = -s.sensorX, -s.sensorZ, -s.sensorY
        end

        M.init = init
        M.reset = reset
        M.getAddress = getAddress
        M.getPort = getPort
        M.getMaxUpdateRate = getMaxUpdateRate
        M.getStructDefinition = getStructDefinition
        M.fillStruct = fillStruct
        M.isPhysicsStepUsed = isPhysicsStepUsed

        return M
        """;
}

/// <summary>
/// Euro Truck Simulator 2 / American Truck Simulator: plugin de telemetria oficial do SCS SDK (src/native/g27scs),
/// copiado para &lt;jogo&gt;\bin\win_x64\plugins. Ao abrir, o jogo avisa uma vez que achou um plugin. Desfazer = apagar o arquivo.
/// </summary>
public static class ScsTelemetry
{
    public const string FileName = "g27hub_telemetry.dll";
    static readonly string[] Games = ["Euro Truck Simulator 2", "American Truck Simulator"];

    public static string SourceDll => Path.Combine(AppContext.BaseDirectory, "x64", FileName);

    /// <summary>Pastas bin\win_x64 dos jogos da SCS instalados pela Steam.</summary>
    public static List<string> GameDirs() =>
        GameScanner.SteamCommonDirs().SelectMany(c => Games.Select(g => Path.Combine(c, g, "bin", "win_x64")))
            .Where(Directory.Exists).ToList();

    static string PluginPath(string binDir) => Path.Combine(binDir, "plugins", FileName);

    public static bool IsInstalled(IEnumerable<string> dirs) => dirs.Any(d => File.Exists(PluginPath(d)));

    public static void Install(IEnumerable<string> dirs, string? source = null)
    {
        source ??= SourceDll;
        if (!File.Exists(source)) throw new FileNotFoundException("Plugin não encontrado (rode src/native/build.ps1 e recompile)", source);
        foreach (var d in dirs)
        {
            Directory.CreateDirectory(Path.Combine(d, "plugins"));
            File.Copy(source, PluginPath(d), overwrite: true);
            AppLog.Ok("TELEMETRIA", $"Plugin de telemetria instalado em {PluginPath(d)}");
        }
    }

    /// <summary>Plugin instalado numa versão antiga: copia o novo. Com o jogo aberto o arquivo está em uso; tenta na próxima abertura do app.</summary>
    public static void UpdateIfOld(IEnumerable<string> dirs, string? source = null)
    {
        source ??= SourceDll;
        if (!File.Exists(source)) return;
        var fresh = File.ReadAllBytes(source);
        foreach (var d in dirs.Where(d => File.Exists(PluginPath(d))))
        {
            if (File.ReadAllBytes(PluginPath(d)).AsSpan().SequenceEqual(fresh)) continue;
            try
            {
                File.Copy(source, PluginPath(d), overwrite: true);
                AppLog.Ok("TELEMETRIA", $"Plugin de telemetria atualizado em {PluginPath(d)}");
            }
            catch (IOException ex) { AppLog.Aviso("TELEMETRIA", $"Plugin do ETS2/ATS não atualizou (jogo aberto?): {ex.Message}"); }
        }
    }

    public static void Uninstall(IEnumerable<string> dirs)
    {
        foreach (var d in dirs.Where(d => File.Exists(PluginPath(d))))
        {
            File.Delete(PluginPath(d));
            AppLog.Info("TELEMETRIA", $"Plugin de telemetria removido de {d}");
        }
    }
}
