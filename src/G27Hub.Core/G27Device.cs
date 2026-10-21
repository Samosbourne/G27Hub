using System.Runtime.InteropServices;
using G27Hub.Logging;
using HidSharp;
using HidSharp.Reports;
using HidSharp.Reports.Input;
using Microsoft.Win32.SafeHandles;
using static G27Hub.Core.G27Commands;

namespace G27Hub.Core;

/// <summary>
/// Conexão com o G27: modo nativo, rotação, LEDs, autocentro e leitura das entradas.
/// Com <see cref="Start"/>, reconecta e reaplica modo nativo + rotação sozinho a cada religada.
/// </summary>
public sealed class G27Device : IDisposable
{
    readonly AutoResetEvent _changed = new(true);
    readonly CancellationTokenSource _cts = new();
    HidStream? _stream;
    ReportDescriptor? _descriptor;
    DeviceItemInputParser? _parser;

    public int Range { get; private set; } = MaxRange;
    public HidDevice? Device { get; private set; }
    public bool IsConnected => _stream != null;
    public G27State? State { get; private set; }

    public event Action<G27State>? StateChanged;
    public event Action<bool>? ConnectionChanged;

    public static IEnumerable<HidDevice> Find() =>
        DeviceList.Local.GetHidDevices(VendorId).Where(d => d.ProductID is PidCompat or PidNative);

    /// <summary>Fica de olho no USB: conecta assim que o G27 aparecer e de novo após cada desconexão.</summary>
    public void Start()
    {
        DeviceList.Local.Changed += OnDeviceListChanged;
        new Thread(Watch) { IsBackground = true, Name = "G27 watch" }.Start();
    }

    void OnDeviceListChanged(object? sender, DeviceListChangedEventArgs e) => _changed.Set();

    void Watch()
    {
        // ponytail: varredura a cada 2 s além do evento do Windows; barato e cobre eventos perdidos.
        while (!_cts.IsCancellationRequested)
        {
            _changed.WaitOne(2000);
            if (_stream == null && !_cts.IsCancellationRequested) Connect();
        }
    }

    /// <summary>Acha o G27, coloca em modo nativo, aplica a rotação e começa a ler. False se não achou.</summary>
    public bool Connect()
    {
        var dev = Find().FirstOrDefault();
        if (dev == null) return false;

        try
        {
            if (dev.ProductID == PidCompat)
            {
                AppLog.Ok("HID", "G27 conectado (046D:C294) → enviando modo nativo");
                using (var s = dev.Open())
                {
                    Write(s, dev, RevertOnReset);
                    try { Write(s, dev, NativeMode); }
                    catch (IOException) { } // o volante some do USB no meio da escrita ao trocar de modo
                }
                dev = WaitFor(PidNative, TimeSpan.FromSeconds(5));
                if (dev == null)
                {
                    AppLog.Aviso("MODO", "G27 não reenumerou para C29B em 5 s; seguindo com o que estiver conectado");
                    dev = Find().FirstOrDefault();
                    if (dev == null) return false;
                }
            }

            var stream = dev.Open();
            stream.ReadTimeout = Timeout.Infinite;
            _descriptor = dev.GetReportDescriptor();
            _parser = _descriptor.DeviceItems[0].CreateDeviceItemInputParser();
            Device = dev;
            _stream = stream;

            Send(G27Commands.Range(Range));
            AppLog.Ok("HID", dev.ProductID == PidNative
                ? $"G27 em modo nativo (046D:C29B), rotação {Range}°"
                : $"G27 conectado em 046D:{dev.ProductID:X4} (NÃO nativo), rotação {Range}°");

            ReadInitialState(dev);
            new Thread(() => ReadLoop(stream)) { IsBackground = true, Name = "G27 read" }.Start();
            ConnectionChanged?.Invoke(true);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Erro("HID", "Falha ao conectar no G27", ex);
            _stream?.Dispose();
            _stream = null;
            return false;
        }
    }

    public void SetRange(int degrees)
    {
        Range = Math.Clamp(degrees, MinRange, MaxRange);
        if (degrees != Range) AppLog.Aviso("HID", $"Rotação {degrees}° fora de {MinRange}–{MaxRange}; usando {Range}°");
        if (_stream == null) return;
        Send(G27Commands.Range(Range));
        AppLog.Ok("HID", $"Rotação aplicada: {Range}°");
    }

    public void SetLeds(int mask)
    {
        Send(Leds(mask));
        AppLog.Info("LED", $"LEDs = {Convert.ToString(mask & 0x1F, 2).PadLeft(5, '0')}");
    }

    public void SetAutocenter(int percent)
    {
        foreach (var cmd in Autocenter(percent)) Send(cmd);
        AppLog.Ok("HID", percent <= 0 ? "Autocentro desligado" : $"Autocentro em {Math.Min(percent, 100)}%");
    }

    /// <summary>Força constante direta no HID (-100..100%), sem DirectInput. 0 para.</summary>
    public void SetForce(int percent)
    {
        Send(percent == 0 ? StopForces : ConstantForce(percent));
        AppLog.Debug("FFB", $"Força direta {percent}%");
    }

    void Send(byte[] cmd)
    {
        if (_stream == null || Device == null) throw new InvalidOperationException("G27 não conectado");
        Write(_stream, Device, cmd);
    }

    static void Write(HidStream s, HidDevice dev, byte[] cmd)
    {
        var buf = new byte[dev.GetMaxOutputReportLength()]; // [0] = report ID 0
        cmd.CopyTo(buf, 1);
        AppLog.Debug("HID", "→ " + Convert.ToHexString(cmd));
        s.Write(buf);
    }

    static HidDevice? WaitFor(int pid, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end)
        {
            var dev = Find().FirstOrDefault(d => d.ProductID == pid);
            if (dev != null) return dev;
            Thread.Sleep(250);
        }
        return null;
    }

    void ReadLoop(HidStream s)
    {
        var buf = new byte[s.Device.GetMaxInputReportLength()];
        try
        {
            while (true)
                if (s.Read(buf, 0, buf.Length) > 0) Parse(buf);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or TimeoutException) { }

        if (Interlocked.CompareExchange(ref _stream, null, s) != s) return; // fechado por nós
        s.Dispose();
        if (!_cts.IsCancellationRequested)
        {
            AppLog.Erro("HID", "G27 desconectado");
            ConnectionChanged?.Invoke(false);
            _changed.Set();
        }
    }

    /// <summary>O G27 só manda relatório quando algo muda; pede o estado atual direto ao driver HID.</summary>
    void ReadInitialState(HidDevice dev)
    {
        var buf = new byte[dev.GetMaxInputReportLength()];
        using var h = CreateFile(dev.DevicePath, 0xC0000000, 3, 0, 3, 0, 0); // leitura+escrita, compartilhado, OPEN_EXISTING
        if (!h.IsInvalid && HidD_GetInputReport(h, buf, buf.Length)) Parse(buf);
        else AppLog.Aviso("HID", $"Não consegui ler o estado inicial (erro {Marshal.GetLastPInvokeError()}); aguardando o primeiro movimento");
    }

    void Parse(byte[] buf)
    {
        if (_descriptor == null || _parser == null) return;
        if (!_descriptor.TryGetReport(ReportType.Input, _descriptor.ReportsUseID ? buf[0] : (byte)0, out var report)) return;
        if (!_parser.TryParseReport(buf, 0, report)) return;

        var axes = new Dictionary<string, AxisValue>();
        var buttons = new List<int>();
        int hat = -1;
        for (int i = 0; i < _parser.ValueCount; i++)
        {
            var v = _parser.GetValue(i);
            var usage = (Usage)v.Usages.FirstOrDefault();
            int val = v.GetLogicalValue();
            if ((uint)usage >> 16 == 0x09) { if (val != 0) buttons.Add((int)((uint)usage & 0xFFFF)); }
            else if (usage == Usage.GenericDesktopHatSwitch) hat = v.IsNull ? -1 : val;
            else axes[usage.ToString().Replace("GenericDesktop", "")] = new(val, v.DataItem.LogicalMinimum, v.DataItem.LogicalMaximum);
        }

        State = new G27State(axes, buttons, hat, Range);
        StateChanged?.Invoke(State);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _changed.Set();
        DeviceList.Local.Changed -= OnDeviceListChanged;
        Interlocked.Exchange(ref _stream, null)?.Dispose();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);

    [DllImport("hid.dll", SetLastError = true)]
    static extern bool HidD_GetInputReport(SafeFileHandle h, byte[] buf, int len);
}
