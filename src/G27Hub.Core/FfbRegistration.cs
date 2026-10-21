using G27Hub.Logging;
using Microsoft.Win32;

namespace G27Hub.Core;

/// <summary>
/// Registra a g27ffb.dll como driver de efeitos DirectInput do G27 nativo, só em HKCU e reversível.
/// COM: HKCU\Software\Classes\CLSID nas visões 64 bits (DLL x64) e 32 bits (DLL x86, jogos 32 bits).
/// DirectInput: chave OEM do C29B → OEMForceFeedback. Num PC sem LGS ela não existe e é criada
/// inteira (efeitos, atributos, eixo atuador); com LGS só o CLSID é trocado. O estado anterior
/// fica em HKCU\Software\G27Hub\FFB para o unregister desfazer exatamente.
/// </summary>
public static class FfbRegistration
{
    public const string Clsid = "{44CD99D0-68D8-40C3-917D-0F52047DFCEB}";
    const string ClsidKey = @"Software\Classes\CLSID\" + Clsid;
    const string OemKey = @"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\VID_046D&PID_C29B";
    const string FfKey = OemKey + @"\OEMForceFeedback";
    const string AxisKey = OemKey + @"\Axes\0";
    public const string ConfigKey = @"Software\G27Hub\FFB";

    // GUIDs padrão do DirectInput; Id = o "dwEffectId" que a DLL recebe em DownloadEffect (ver effects.c).
    const uint Force = 0x8601, Ramp = 0x8602, Periodic = 0x8603, Condition = 0xF804; // DIEFT_* + envelope/startdelay/saturação/deadband
    static readonly (string Guid, string Name, uint Id, uint Type)[] Effects =
    [
        ("{13541C20-8E33-11D0-9AD0-00A0C9A06E35}", "Constant Force", 0, Force),
        ("{13541C21-8E33-11D0-9AD0-00A0C9A06E35}", "Ramp Force", 1, Ramp),
        ("{13541C22-8E33-11D0-9AD0-00A0C9A06E35}", "Square", 2, Periodic),
        ("{13541C23-8E33-11D0-9AD0-00A0C9A06E35}", "Sine", 3, Periodic),
        ("{13541C24-8E33-11D0-9AD0-00A0C9A06E35}", "Triangle", 4, Periodic),
        ("{13541C25-8E33-11D0-9AD0-00A0C9A06E35}", "Sawtooth Up", 5, Periodic),
        ("{13541C26-8E33-11D0-9AD0-00A0C9A06E35}", "Sawtooth Down", 6, Periodic),
        ("{13541C27-8E33-11D0-9AD0-00A0C9A06E35}", "Spring", 7, Condition),
        ("{13541C28-8E33-11D0-9AD0-00A0C9A06E35}", "Damper", 8, Condition),
        ("{13541C29-8E33-11D0-9AD0-00A0C9A06E35}", "Inertia", 9, Condition),
        ("{13541C2A-8E33-11D0-9AD0-00A0C9A06E35}", "Friction", 10, Condition),
    ];

    public static string DllPath(string arch) => Path.Combine(AppContext.BaseDirectory, arch, "g27ffb.dll");

    static RegistryKey Hkcu(RegistryView view = RegistryView.Registry64) => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);

    public static bool Register()
    {
        foreach (var arch in new[] { "x64", "x86" })
            if (!File.Exists(DllPath(arch)))
            {
                AppLog.Erro("FFB", $"DLL não encontrada: {DllPath(arch)} (rode src/native/build.ps1 e recompile)");
                return false;
            }

        using var hkcu = Hkcu();
        using var oem = hkcu.OpenSubKey(OemKey, writable: true);
        if (oem == null)
        {
            AppLog.Erro("FFB", "Chave OEM do G27 nativo não existe: conecte o volante e rode 'g27cli native' antes");
            return false;
        }
        using var cfg = hkcu.CreateSubKey(ConfigKey);

        // Guarda o estado original só na primeira vez (registrar de novo não perde o backup).
        if (cfg.GetValue("OrigState") == null)
        {
            using var ff = hkcu.OpenSubKey(FfKey);
            cfg.SetValue("OrigState", ff == null ? "criada" : "existia");
            cfg.SetValue("OrigClsid", ff?.GetValue("CLSID") as string ?? "");
            using var axis = hkcu.OpenSubKey(AxisKey);
            cfg.SetValue("AxisCriado", axis == null ? 1 : 0);
        }

        WriteCom(RegistryView.Registry64, DllPath("x64"));
        WriteCom(RegistryView.Registry32, DllPath("x86"));

        using (var ff = hkcu.CreateSubKey(FfKey))
        {
            ff.SetValue("CLSID", Clsid);
            if ((string)cfg.GetValue("OrigState")! == "criada")
            {
                ff.SetValue("Attributes", Dwords(0, 1000, 1000), RegistryValueKind.Binary); // DIFFDEVICEATTRIBUTES
                foreach (var e in Effects)
                {
                    using var k = ff.CreateSubKey($@"Effects\{e.Guid}");
                    k.SetValue("", e.Name);
                    // DIEFFECTATTRIBUTES: id, tipo, parâmetros estáticos e dinâmicos (DIEP_ALLPARAMS), coordenadas (cartesiano/polar/esférico)
                    k.SetValue("Attributes", Dwords(e.Id, e.Type, 0x3FF, 0x3FF, 0x70), RegistryValueKind.Binary);
                }
            }
        }

        if ((int)cfg.GetValue("AxisCriado", 0)! == 1)
        {
            using var axis = hkcu.CreateSubKey(AxisKey);
            // DIOBJECTATTRIBUTES: atuador de FFB | posição | polled; usage page 1, usage 0x30 (X = volante)
            axis.SetValue("Attributes", new byte[] { 0x01, 0x81, 0x00, 0x00, 0x01, 0x00, 0x30, 0x00 }, RegistryValueKind.Binary);
        }

        AppLog.Ok("FFB", $"Driver de FFB registrado (CLSID {Clsid}, x64 + x86)");
        return true;
    }

    public static void Unregister()
    {
        using var hkcu = Hkcu();
        using var cfg = hkcu.OpenSubKey(ConfigKey, writable: true);
        var state = cfg?.GetValue("OrigState") as string;

        if (state == "criada") hkcu.DeleteSubKeyTree(FfKey, throwOnMissingSubKey: false);
        else if (state == "existia")
        {
            using var ff = hkcu.OpenSubKey(FfKey, writable: true);
            var orig = cfg!.GetValue("OrigClsid") as string;
            if (string.IsNullOrEmpty(orig)) ff?.DeleteValue("CLSID", throwOnMissingValue: false);
            else ff?.SetValue("CLSID", orig);
        }

        if (cfg != null && (int)cfg.GetValue("AxisCriado", 0)! == 1)
        {
            hkcu.DeleteSubKeyTree(AxisKey, throwOnMissingSubKey: false);
            using var axes = hkcu.OpenSubKey(OemKey + @"\Axes");
            if (axes is { SubKeyCount: 0, ValueCount: 0 }) hkcu.DeleteSubKey(OemKey + @"\Axes", throwOnMissingSubKey: false);
        }

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = Hkcu(view);
            root.DeleteSubKeyTree(ClsidKey, throwOnMissingSubKey: false);
        }
        foreach (var name in new[] { "OrigState", "OrigClsid", "AxisCriado" }) cfg?.DeleteValue(name, throwOnMissingValue: false);

        AppLog.Ok("FFB", state == null ? "Driver de FFB removido (não havia backup; só o CLSID foi apagado)" : "Driver de FFB removido; registro restaurado");
    }

    /// <summary>Linhas de diagnóstico: DLLs registradas, CLSID da chave OEM, configuração.</summary>
    public static IEnumerable<string> Status()
    {
        foreach (var (view, arch) in new[] { (RegistryView.Registry64, "x64"), (RegistryView.Registry32, "x86") })
        {
            using var k = Hkcu(view).OpenSubKey(ClsidKey + @"\InProcServer32");
            var path = k?.GetValue("") as string;
            yield return $"DLL {arch}: {(path == null ? "não registrada" : path + (File.Exists(path) ? "" : "  <-- ARQUIVO NÃO EXISTE"))}";
        }
        using var hkcu = Hkcu();
        using var ff = hkcu.OpenSubKey(FfKey);
        var clsid = ff?.GetValue("CLSID") as string;
        yield return $"Chave OEM C29B: {(ff == null ? "sem OEMForceFeedback" : clsid == Clsid ? "aponta para o G27 Hub" : $"aponta para {clsid ?? "nada"}")}";
        using var cfg = hkcu.OpenSubKey(ConfigKey);
        yield return $"Config: Log={cfg?.GetValue("Log") ?? 1} MasterGain={cfg?.GetValue("MasterGain") ?? 100}% Invert={cfg?.GetValue("Invert") ?? 0}";
    }

    public static void SetConfig(string name, int value)
    {
        using var cfg = Hkcu().CreateSubKey(ConfigKey);
        cfg.SetValue(name, value, RegistryValueKind.DWord);
        AppLog.Ok("FFB", $"{name} = {value} (vale ao reabrir o jogo)");
    }

    static void WriteCom(RegistryView view, string dll)
    {
        using var root = Hkcu(view);
        using var k = root.CreateSubKey(ClsidKey);
        k.SetValue("", "G27 Hub - driver de efeitos DirectInput");
        using var ip = k.CreateSubKey("InProcServer32");
        ip.SetValue("", dll);
        ip.SetValue("ThreadingModel", "Both");
    }

    static byte[] Dwords(params uint[] v) => v.SelectMany(BitConverter.GetBytes).ToArray();
}
