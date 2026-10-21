using Microsoft.Win32;

namespace G27Hub.Core;

/// <summary>Verificações do sistema para os testes rápidos do console (só leitura).</summary>
public static class Diagnostics
{
    // Serviços/drivers deixados pelo LGS (LG*) e pelo G HUB (logi_joy_*), que brigam com o Modo Padrão.
    static readonly string[] OldLogitechServices =
    [
        "LGBusEnum", "LGVirHid", "LGJoyXlCore", "LGSHidFilt", "LGJoyHidFilter",
        "logi_joy_bus_enum", "logi_joy_hid_filter", "logi_joy_hid_lo", "logi_joy_vir_hid", "logi_joy_xlcore", "logi_generic_hid_filter",
    ];

    /// <summary>Integridade de Memória (HVCI): true ligada, false desligada, null não deu para ler.</summary>
    public static bool? MemoryIntegrity()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
        return k?.GetValue("Enabled") is int v ? v == 1 : null;
    }

    public static string[] OldLogitechDrivers() => OldLogitechServices.Where(ServiceExists).ToArray();

    public static bool VJoyInstalled() => ServiceExists("vjoy");
    public static bool HidHideInstalled() => ServiceExists("HidHide");

    static bool ServiceExists(string name)
    {
        using var k = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
        return k != null;
    }
}
