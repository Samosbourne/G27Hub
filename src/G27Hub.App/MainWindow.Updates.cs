using System.Diagnostics;
using System.Reflection;
using System.Windows;
using G27Hub.Core;
using G27Hub.Logging;
using static G27Hub.Core.Lang;

namespace G27Hub.App;

/// <summary>Aviso de versão nova pelo GitHub (Fase 10). Desligado de fábrica; só avisa e abre a página de download.</summary>
public partial class MainWindow
{
    static Version AppVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);

    void UpdateCheck_Click(object sender, RoutedEventArgs e)
    {
        _config.UpdateCheckOn = UpdateCheck.IsChecked == true;
        _config.Save();
        AppLog.Info("APP", _config.UpdateCheckOn ? "Aviso de atualização ligado" : "Aviso de atualização desligado");
        if (_config.UpdateCheckOn) _ = CheckUpdates(manual: true);
    }

    /// <summary>Ao abrir: só se ligado e se a última procura foi há mais de 1 dia.</summary>
    void MaybeCheckUpdates()
    {
        if (_config.UpdateCheckOn && DateTime.UtcNow - _config.LastUpdateCheck > TimeSpan.FromDays(1)) _ = CheckUpdates(manual: false);
    }

    async Task CheckUpdates(bool manual)
    {
        var found = await Updates.CheckAsync(AppVersion);
        _config.LastUpdateCheck = DateTime.UtcNow;
        _config.Save();
        if (found is { } f)
            ShowToast("", "Ok", T($"G27 Hub {f.Version.ToString(3)} disponível"), T($"Você está com a {AppVersion.ToString(3)}. Baixe o instalador novo e rode por cima; perfis e ajustes ficam."),
                TimeSpan.FromSeconds(20), (T("Baixar"), () => Process.Start(new ProcessStartInfo(f.Url) { UseShellExecute = true })), (T("Depois"), null));
        else if (manual)
            ShowToast("", "Ok", T("Você está com a versão mais nova"), T($"G27 Hub {AppVersion.ToString(3)}."));
    }
}
