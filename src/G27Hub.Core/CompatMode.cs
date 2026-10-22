using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using G27Hub.Logging;

namespace G27Hub.Core;

/// <summary>Instalador oficial de um driver da Compatibilidade Total, fixado por versão e SHA-256.</summary>
public sealed record DriverInstaller(string Name, string Url, string Sha256, string SilentArgs, string Publisher, Func<bool> IsInstalled);

/// <summary>
/// Fase 8a: instala e desinstala o vJoy (volante virtual) e o HidHide (esconde o G27 dos jogos), só quando o usuário pede.
/// Baixa os instaladores oficiais do GitHub, confere o SHA-256 e a assinatura, cria um ponto de restauração e roda tudo
/// num PowerShell elevado (UAC). Tudo reversível: Uninstall remove os dois; depois é preciso reiniciar.
/// </summary>
public static class CompatMode
{
    public static readonly DriverInstaller VJoy = new("vJoy 2.2.2.0",
        "https://github.com/BrunnerInnovation/vJoy/releases/download/v2.2.2.0/vJoySetup_v2.2.2.0_Win10_Win11.exe",
        "ef569a3105cd301b89580f18f60c66b339e95296acf2c0dfcaf4b4bbf8ab68fe",
        "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART", "Brunner Elektronik AG", Diagnostics.VJoyInstalled); // Inno Setup

    public static readonly DriverInstaller HidHide = new("HidHide 1.5.230",
        "https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/HidHide_1.5.230_x64.exe",
        "f4bbbcb82e6258641b887c74bc81c4c5f66e4aa811808dfc304347687b7605f6",
        "/exenoui /qn /norestart", "Nefarius Software Solutions e.U.", Diagnostics.HidHideInstalled); // Advanced Installer

    public static readonly DriverInstaller[] All = [VJoy, HidHide];

    public static bool IsInstalled => All.All(d => d.IsInstalled());

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "G27Hub", "compat");

    /// <summary>Baixa o instalador (ou reaproveita um já baixado) e confere o SHA-256. Lança se não bater.</summary>
    public static async Task<string> Download(DriverInstaller d, HttpClient http, IProgress<double>? progress = null)
    {
        Directory.CreateDirectory(Dir);
        var path = Path.Combine(Dir, Path.GetFileName(new Uri(d.Url).LocalPath));
        if (!File.Exists(path) || Sha256(path) != d.Sha256)
        {
            AppLog.Info("MODO", $"Baixando {d.Name} de {d.Url}");
            using var resp = await http.GetAsync(d.Url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? 0, done = 0;
            await using (var s = await resp.Content.ReadAsStreamAsync())
            await using (var f = File.Create(path))
            {
                var buf = new byte[81920];
                for (int n; (n = await s.ReadAsync(buf)) > 0;)
                {
                    await f.WriteAsync(buf.AsMemory(0, n));
                    done += n;
                    if (total > 0) progress?.Report((double)done / total);
                }
            }
        }
        var sha = Sha256(path);
        if (sha != d.Sha256)
        {
            File.Delete(path);
            throw new InvalidDataException($"{d.Name}: o arquivo baixado não é o oficial conferido (SHA-256 {sha}).");
        }
        AppLog.Ok("MODO", $"{d.Name} baixado e conferido (SHA-256)");
        return path;
    }

    public static string Sha256(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(f));
    }

    /// <summary>Script elevado: ponto de restauração (aborta sem ele), assinatura de cada instalador e instalação.</summary>
    public static string InstallScript(IEnumerable<(DriverInstaller D, string Path)> items, string log)
    {
        var steps = string.Join("\n", items.Select(x => $"""
              Install-One {Q(x.D.Name)} {Q(x.Path)} {Q(x.D.SilentArgs)} {Q(x.D.Publisher)}
            """));
        return Header(log) + $$"""
            function Install-One($name, $path, $argList, $publisher) {
              $sig = Get-AuthenticodeSignature $path
              if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notlike "*O=$publisher*") { throw "$name com assinatura inválida ($($sig.Status))" }
              Log "INFO Instalando $name (assinado por $publisher)"
              $p = Start-Process $path -ArgumentList $argList -Wait -PassThru
              if ($p.ExitCode -notin 0, 1641, 3010) { throw "$name terminou com código $($p.ExitCode)" }
              Log "OK $name instalado"
            }
            try {
              Log 'INFO Criando ponto de restauração do Windows'
              $k = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'
              $old = (Get-ItemProperty $k -ErrorAction SilentlyContinue).SystemRestorePointCreationFrequency
              Set-ItemProperty $k -Name SystemRestorePointCreationFrequency -Value 0 -Type DWord   # o Windows só deixa 1 por dia
              try { Enable-ComputerRestore -Drive "$env:SystemDrive\" } catch { }
              try { Checkpoint-Computer -Description 'G27 Hub: antes do vJoy e do HidHide' -RestorePointType APPLICATION_INSTALL }
              finally { if ($null -eq $old) { Remove-ItemProperty $k -Name SystemRestorePointCreationFrequency } else { Set-ItemProperty $k -Name SystemRestorePointCreationFrequency -Value $old } }
              Log 'OK Ponto de restauração criado'
            {{steps}}
            {{(items.Any(x => x.D == VJoy) ? ConfigStep : "")}}
              exit 0
            } catch { Log "ERRO $($_.Exception.Message)"; exit 1 }
            """;
    }

    /// <summary>Script elevado: configura o dispositivo 1 do vJoy (eixos, botões, POV e FFB que o G27 Hub usa).</summary>
    public static string ConfigScript(string log) => Header(log) + $$"""
        try {
        {{ConfigStep}}
          exit 0
        } catch { Log "ERRO $($_.Exception.Message)"; exit 1 }
        """;

    static string ConfigStep => $$"""
          Log 'INFO Configurando o volante virtual (vJoy 1)'
          $out = & "$env:ProgramFiles\vJoy\x64\vJoyConfig.exe" {{VirtualWheel.ConfigArgs}} 2>&1 | Out-String
          if ($LASTEXITCODE -ne 0) { throw "vJoyConfig terminou com código $($LASTEXITCODE): $out" }
          Log 'OK Volante virtual configurado'
        """;

    /// <summary>Script elevado: desinstala vJoy e HidHide pelo registro de programas instalados.</summary>
    public static string UninstallScript(string log) => Header(log) + """
        try {
          $hhcli = "$env:ProgramFiles\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe"
          if (Test-Path $hhcli) { & $hhcli --cloak-off | Out-Null }   # o G27 volta a aparecer já, antes do reinício
          $apps = Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall' |
            Get-ItemProperty | Where-Object { $_.DisplayName -like 'vJoy*' -or $_.DisplayName -like 'HidHide*' }
          foreach ($a in $apps) {
            Log "INFO Desinstalando $($a.DisplayName)"
            if ($a.UninstallString -match 'msiexec' -or $a.WindowsInstaller -eq 1) {
              $p = Start-Process msiexec.exe -ArgumentList "/x $($a.PSChildName) /qn /norestart" -Wait -PassThru
            } else {
              $exe = ($a.UninstallString -replace '^"([^"]+)".*$', '$1')
              $p = Start-Process $exe -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -Wait -PassThru
            }
            if ($p.ExitCode -notin 0, 1605, 1641, 3010) { throw "$($a.DisplayName) terminou com código $($p.ExitCode)" }
            Log "OK $($a.DisplayName) desinstalado"
          }
          exit 0
        } catch { Log "ERRO $($_.Exception.Message)"; exit 1 }
        """;

    static string Header(string log) => $$"""
        $ErrorActionPreference = 'Stop'
        function Log($m) { Add-Content -LiteralPath {{Q(log)}} -Value $m -Encoding UTF8 }

        """;

    static string Q(string s) => "'" + s.Replace("'", "''") + "'";

    /// <summary>
    /// Roda o script num PowerShell elevado (o Windows mostra o UAC), espera e repassa o log dele ao AppLog.
    /// Cada linha do log também vai para onLine assim que o script escreve (barra de progresso).
    /// Retorna o código de saída; null se o usuário recusou o UAC.
    /// </summary>
    public static int? RunElevated(string name, Func<string, string> scriptForLog, Action<string>? onLine = null)
    {
        Directory.CreateDirectory(Dir);
        var ps1 = Path.Combine(Dir, name + ".ps1");
        var log = Path.Combine(Dir, name + ".log");
        File.Delete(log);
        File.WriteAllText(ps1, scriptForLog(log), new System.Text.UTF8Encoding(true)); // BOM: o PowerShell 5.1 lê acentos certo
        try
        {
            using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{ps1}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            })!;
            int seen = 0;
            void Pump(bool final = false)
            {
                string[] lines;
                try
                {
                    using var r = new StreamReader(new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                    var text = r.ReadToEnd();
                    if (!final) text = text[..(text.LastIndexOf('\n') + 1)]; // linha pela metade espera a próxima leitura
                    lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                }
                catch (IOException) { return; } // ainda não existe ou está sendo escrito
                for (; seen < lines.Length; seen++)
                {
                    var line = lines[seen];
                    var (level, msg) = line.IndexOf(' ') is > 0 and var i ? (line[..i], line[(i + 1)..]) : ("INFO", line);
                    switch (level)
                    {
                        case "OK": AppLog.Ok("MODO", msg); break;
                        case "ERRO": AppLog.Erro("MODO", msg); break;
                        default: AppLog.Info("MODO", msg); break;
                    }
                    onLine?.Invoke(line);
                }
            }
            while (!p.WaitForExit(250)) Pump();
            Pump(final: true);
            return p.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: recusou o UAC
        {
            AppLog.Aviso("MODO", "Permissão de administrador recusada; nada foi instalado ou removido");
            return null;
        }
    }
}
