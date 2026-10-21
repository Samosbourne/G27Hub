using System.Windows;
using G27Hub.Logging;
using Forms = System.Windows.Forms;

namespace G27Hub.App;

public partial class App : Application
{
    Mutex? _single;
    Forms.NotifyIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // --shots <pasta> [--live]: salva PNG de cada tela e sai (revisão visual sem abrir a janela na frente;
        // --live lê o G27 conectado)
        var shots = Array.IndexOf(e.Args, "--shots");
        if (shots >= 0)
        {
            AppLog.Init();
            var w = new MainWindow(connect: false)
            {
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, ShowActivated = false,
            };
            w.Show();
            w.SaveShots(e.Args.ElementAtOrDefault(shots + 1) ?? "shots", e.Args.Contains("--live"));
            AppLog.Close();
            Shutdown();
            return;
        }

        _single = new Mutex(true, @"Local\G27Hub_App", out var first);
        if (!first)
        {
            MessageBox.Show("O G27 Hub já está aberto (veja o ícone perto do relógio).", "G27 Hub");
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
        var tray = new Forms.NotifyIcon { Icon = window.TrayIcon, Text = "G27 Hub", Visible = true };
        tray.ContextMenuStrip = new Forms.ContextMenuStrip();
        tray.ContextMenuStrip.Items.Add("Abrir G27 Hub", null, (_, _) => window.ShowFromTray());
        tray.ContextMenuStrip.Items.Add("Reaplicar modo nativo e rotação", null, (_, _) => window.Reapply());
        tray.ContextMenuStrip.Items.Add("-");
        tray.ContextMenuStrip.Items.Add("Sair", null, (_, _) => window.Quit());
        tray.DoubleClick += (_, _) => window.ShowFromTray();
        return tray;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray != null) _tray.Visible = false;
        _tray?.Dispose();
        AppLog.Info("APP", "G27 Hub fechado");
        AppLog.Close();
        base.OnExit(e);
    }
}
