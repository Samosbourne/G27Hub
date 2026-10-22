using System.Diagnostics;
using System.Windows;
using G27Hub.Core;
using G27Hub.Logging;
using static G27Hub.Core.Lang;

namespace G27Hub.App;

/// <summary>Botão do globo no topo: Português / English. Trocar salva e reabre o app no outro idioma.</summary>
public partial class MainWindow
{
    void Lang_Click(object sender, RoutedEventArgs e) => LangPopup.IsOpen = true;

    void LangPick_Click(object sender, RoutedEventArgs e)
    {
        LangPopup.IsOpen = false;
        var lang = (string)((FrameworkElement)sender).Tag;
        if (lang == Current) return;
        var ask = lang == "en" ? "Switch the app to English? It will reopen now." : "Trocar o app para português? Ele vai reabrir agora.";
        if (MessageBox.Show(this, ask, "G27 Hub", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _config.Language = lang;
        _config.Save();
        AppLog.Info("APP", $"Idioma trocado para {lang}; reabrindo");
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--restart") { UseShellExecute = false });
        Quit();
    }
}
