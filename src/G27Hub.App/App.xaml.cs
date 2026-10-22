using System.Windows;
using G27Hub.Logging;
using Forms = System.Windows.Forms;
using static G27Hub.Core.Lang;

namespace G27Hub.App;

public partial class App : Application
{
    Mutex? _single;
    Forms.NotifyIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (Array.IndexOf(e.Args, "--export-icon") is >= 0 and var ico)
        {
            G27Hub.App.MainWindow.ExportIcon(e.Args[ico + 1]);
            Shutdown();
            return;
        }
        // desinstalador (Inno Setup): desfaz o que o app mudou no PC antes de apagar os arquivos
        if (e.Args.Contains("--uninstall-cleanup"))
        {
            AppLog.Init();
            UninstallCleanup.Run();
            AppLog.Close();
            Shutdown();
            return;
        }

        // --shots <pasta> [--live]: salva PNG de cada tela e sai (revisão visual sem abrir a janela na frente;
        // --live lê o G27 conectado)
        var shots = Array.IndexOf(e.Args, "--shots");
        if (shots >= 0)
        {
            AppLog.Init();
            var lang = Array.IndexOf(e.Args, "--lang");
            if (lang >= 0) G27Hub.Core.Lang.Set(e.Args.ElementAtOrDefault(lang + 1) ?? "pt"); // "--lang en": prints em inglês + lista do que falta traduzir
            var w = new MainWindow(connect: false)
            {
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, ShowActivated = false,
            };
            w.Show();
            var dir = e.Args.ElementAtOrDefault(shots + 1) ?? "shots";
            w.SaveShots(dir, e.Args.Contains("--live"));
            if (G27Hub.Core.Lang.English)
                System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "faltando-en.txt"), G27Hub.Core.Lang.Missing.Order());
            AppLog.Close();
            Shutdown();
            return;
        }

        // primeira abertura depois do instalador: o idioma escolhido no instalador vira o do app
        if (Array.IndexOf(e.Args, "--setup-lang") is >= 0 and var sl && !System.IO.File.Exists(G27Hub.Core.AppConfig.DefaultPath))
        {
            var cfg = G27Hub.Core.AppConfig.Load();
            cfg.Language = e.Args.ElementAtOrDefault(sl + 1) == "en" ? "en" : "pt";
            cfg.Save();
        }
        G27Hub.Core.Lang.Set(G27Hub.Core.AppConfig.Load().Language);
        _single = new Mutex(false, @"Local\G27Hub_App");
        bool first;
        try { first = _single.WaitOne(e.Args.Contains("--restart") ? 8000 : 0); } // trocando de idioma: espera a instância anterior fechar
        catch (AbandonedMutexException) { first = true; }
        if (!first)
        {
            MessageBox.Show(T("O G27 Hub já está aberto (veja o ícone perto do relógio)."), T("G27 Hub"));
            Shutdown();
            return;
        }

        AppLog.Init();
        FfbLogServer.Start();
        AppLog.Info("APP", "G27 Hub iniciado");
        DispatcherUnhandledException += (_, ex) =>
        {
            AppLog.Erro("APP", "Erro inesperado na interface", ex.Exception);
            ex.Handled = true;
        };

        var window = new MainWindow(connect: true) { AutoFfbTest = e.Args.Contains("--ffbtest") };
        MainWindow = window;
        _tray = CreateTray(window);
        if (!e.Args.Contains("--minimized")) window.Show();
    }

    static Forms.NotifyIcon CreateTray(MainWindow window)
    {
        var tray = new Forms.NotifyIcon { Icon = window.TrayIcon, Text = T("G27 Hub"), Visible = true };
        tray.ContextMenuStrip = new Forms.ContextMenuStrip();
        tray.ContextMenuStrip.Items.Add(T("Abrir G27 Hub"), null, (_, _) => window.ShowFromTray());
        tray.ContextMenuStrip.Items.Add(T("Reaplicar modo nativo e rotação"), null, (_, _) => window.Reapply());
        tray.ContextMenuStrip.Items.Add("-");
        tray.ContextMenuStrip.Items.Add(T("Sair"), null, (_, _) => window.Quit());
        tray.DoubleClick += (_, _) => window.ShowFromTray();
        return tray;
    }

    /// <summary>Notificação do Windows (balão da bandeja), para quando a janela está escondida.</summary>
    public void Balloon(string title, string text) => _tray?.ShowBalloonTip(6000, title, text, Forms.ToolTipIcon.Info);

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray != null) _tray.Visible = false;
        _tray?.Dispose();
        AppLog.Info("APP", "G27 Hub fechado");
        AppLog.Close();
        base.OnExit(e);
    }
}
