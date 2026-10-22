using G27Hub.Logging;
using Microsoft.Win32;

namespace G27Hub.App;

/// <summary>
/// Iniciar com o Windows: entrada em HKCU\...\Run (sem admin), abre minimizado na bandeja.
/// O resto da configuração (perfis, bandeja, notificações) mora em G27Hub.Core.AppConfig.
/// </summary>
public static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool Enabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue("G27Hub") != null;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue("G27Hub", $"\"{Environment.ProcessPath}\" --minimized");
            else k.DeleteValue("G27Hub", throwOnMissingValue: false);
            AppLog.Ok("APP", value ? "Vai iniciar com o Windows" : "Não inicia mais com o Windows");
        }
    }
}
