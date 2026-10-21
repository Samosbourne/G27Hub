using System.IO;
using System.Text.Json;
using G27Hub.Logging;
using Microsoft.Win32;

namespace G27Hub.App;

/// <summary>
/// Ajustes do volante do perfil "Desktop: Padrão", em %APPDATA%\G27Hub\config.json.
/// ponytail: um perfil só; a Fase 5 troca isto por perfis por jogo.
/// Os ajustes de FFB moram no registro (HKCU\Software\G27Hub\FFB), que é o que a DLL lê.
/// </summary>
public sealed class Settings
{
    public int Range { get; set; } = 900;
    public int Autocenter { get; set; } = 20;
    public bool AutocenterOn { get; set; } = true;
    public bool CloseToTray { get; set; } = true;

    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "G27Hub", "config.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex)
        {
            AppLog.Aviso("PERFIL", $"config.json inválido, usando o padrão ({ex.Message})");
        }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    // Iniciar com o Windows: entrada em HKCU\...\Run (sem admin), abre minimizado na bandeja.
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool StartWithWindows
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
