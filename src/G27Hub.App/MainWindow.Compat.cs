using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using G27Hub.Core;
using G27Hub.Logging;
using static G27Hub.Core.Lang;

namespace G27Hub.App;

/// <summary>
/// Fase 8a: Modo Compatibilidade Total, opcional. Só instala os drivers (vJoy + HidHide) quando o usuário clica em
/// ATIVAR e confirma; DESINSTALAR remove os dois. Nada fica escondido nem virtual só por instalar (isso é a Fase 8b).
/// </summary>
public partial class MainWindow
{
    bool _compatBusy;
    volatile VirtualWheel? _vwheel;

    // ================= Fase 8b: ligar/desligar o modo =================

    void StartCompat()
    {
        _dev.StateChanged += s => _vwheel?.Feed(s, W); // thread de leitura do volante, a cada relatório
        _dev.ConnectionChanged += c =>
        {
            if (c && _vwheel != null) Dispatcher.BeginInvoke(() => { Try(HidHide.HideG27); StartVJoyFfb(); }); // religado = outro caminho
        };
        if (_config.CompatOn)
        {
            if (CompatMode.IsInstalled && !VirtualWheel.NeedsConfig()) _ = SetCompat(true);
            else { _config.CompatOn = false; _config.Save(); AppLog.Aviso("VJOY", "Compatibilidade Total estava ligada mas os drivers não estão prontos; voltando ao Modo Padrão"); }
        }
        UpdateCompatVisual();
    }

    /// <summary>Ao fechar o app: o G27 volta a aparecer para os jogos (sem o app, o vJoy ficaria parado). O modo religa ao abrir.</summary>
    void StopCompatForExit()
    {
        _vffb?.Dispose();
        _vffb = null;
        var v = _vwheel;
        if (v == null) return;
        _vwheel = null;
        v.Dispose();
        Try(HidHide.ShowG27);
    }

    VJoyFfb? _vffb;

    /// <summary>8c: FFB do jogo (pelo vJoy) → motor da g27ffb.dll → G27. Sem volante conectado, liga ao conectar.</summary>
    void StartVJoyFfb()
    {
        _vffb?.Dispose();
        _vffb = null;
        if (_dev.Device is not { } d || !_dev.IsConnected) return;
        try { _vffb = VJoyFfb.Start(d.DevicePath, FfbRegistration.DllPath("x64")); }
        catch (Exception ex) { AppLog.Erro("FFB", "Force feedback do vJoy não ligou (o volante virtual funciona sem força)", ex); }
    }

    void Try(Action a)
    {
        try { a(); }
        catch (Exception ex) { AppLog.Erro("VJOY", "Falha no HidHide", ex); }
    }

    async void Mode_Click(object sender, RoutedEventArgs e)
    {
        bool on = ModeCompat.IsChecked == true;
        if (on == (_vwheel != null)) return;
        DevicePopup.IsOpen = false;
        await SetCompat(on);
    }

    async Task SetCompat(bool on)
    {
        try
        {
            if (on)
            {
                if (!CompatMode.IsInstalled)
                {
                    UpdateCompatVisual();
                    Compat_Click(this, new RoutedEventArgs()); // oferece instalar os drivers (com confirmação)
                    return;
                }
                if (VirtualWheel.NeedsConfig())
                {
                    if (MessageBox.Show(this, T("Falta configurar o volante virtual (vJoy) com os eixos, botões, direcional e force feedback do G27.\n\nÉ uma vez só: o Windows vai pedir permissão de administrador. Continuar?"),
                            T("G27 Hub: Compatibilidade Total"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) { UpdateCompatVisual(); return; }
                    ShowCompatProgress(T("Configurando o volante virtual"), 1);
                    CompatStep(T("Confirme a permissão de administrador do Windows."), creep: false);
                    var code = await Task.Run(() => CompatMode.RunElevated("configurar", CompatMode.ConfigScript, CompatLine));
                    await HideCompatProgress(code == 0);
                    if (code != 0 || VirtualWheel.NeedsConfig())
                    {
                        if (code == 0) AppLog.Erro("VJOY", $"vJoy configurado, mas ainda incompleto: {VirtualWheel.Describe()}");
                        if (code != null) ShowToast("VJOY", "Error", T("Volante virtual não configurado"), T("Veja os detalhes no console."), T("Ver detalhes"));
                        UpdateCompatVisual();
                        return;
                    }
                }
                var v = new VirtualWheel();
                if (!v.Start()) { ShowToast("VJOY", "Error", T("Volante virtual não ligou"), T("Veja os detalhes no console."), T("Ver detalhes")); UpdateCompatVisual(); return; }
                try { HidHide.HideG27(); }
                catch { v.Dispose(); throw; }
                _vwheel = v;
                if (_dev.State is { } s) v.Feed(s, W);
                StartVJoyFfb();
                _config.CompatOn = true;
                AppLog.Ok("MODO", "Compatibilidade Total ligada: os jogos veem o vJoy Device");
                ShowToast("VJOY", "Accent", T("Compatibilidade Total ligada"), T("Os jogos agora veem o \"vJoy Device\". Reabra o jogo e configure os controles nele."));
            }
            else
            {
                StopCompatForExit();
                _config.CompatOn = false;
                AppLog.Ok("MODO", "Modo Padrão: os jogos veem o G27 de novo");
                ShowToast("G27", "Ok", T("Modo Padrão"), T("Os jogos veem o G27 de novo. Reabra o jogo se ele estiver aberto."));
            }
            _config.Save();
        }
        catch (Exception ex)
        {
            AppLog.Erro("VJOY", "Falha ao trocar de modo", ex);
            ShowToast("VJOY", "Error", T("Falha ao trocar de modo"), ex.Message, T("Ver detalhes"));
        }
        UpdateCompatVisual();
    }

    /// <summary>Borda vermelha + selo no topo quando a Compatibilidade Total está ligada; libera as sensibilidades.</summary>
    void UpdateCompatVisual()
    {
        bool on = _vwheel != null;
        ModeCompat.IsChecked = on;
        ModeStandard.IsChecked = !on;
        CompatFrame.Visibility = CompatBadge.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        foreach (var s in new[] { SteerSensSlider, ClutchSensSlider, BrakeSensSlider, ThrottleSensSlider }) s.IsEnabled = on;
        VolanteModeHint.Text = on
            ? T(": os jogos veem o vJoy Device. A sensibilidade vale aqui; a rotação e a mola continuam no G27.")
            : T(": o jogo vê o G27 de verdade. A sensibilidade só existe no Modo Compatibilidade Total.");
        PedaisModeHint.Text = on
            ? T("Compatibilidade Total ligada: as sensibilidades valem no volante virtual (vJoy).")
            : T("Sensibilidade dos pedais só no Modo Compatibilidade Total. Pedais combinados no Modo Padrão usam o modo antigo (Driving Force EX) e o jogo perde a embreagem.");
        UpdateDeviceStatus();
    }

    // barra de progresso: passos concluídos + fração do passo atual (download real, ou "andando" devagar enquanto espera)
    readonly DispatcherTimer _compatTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    int _compatSteps, _compatDone;
    double _compatFrac, _compatShown;
    bool _compatCreep;

    void ShowCompatProgress(string title, int steps)
    {
        (_compatSteps, _compatDone, _compatFrac, _compatShown, _compatCreep) = (steps, 0, 0, 0, true);
        CompatTitle.Text = title;
        CompatOverlay.Visibility = Visibility.Visible;
        _compatTimer.Tick -= CompatTick;
        _compatTimer.Tick += CompatTick;
        _compatTimer.Start();
        CompatTick(null, EventArgs.Empty);
    }

    void CompatTick(object? sender, EventArgs e)
    {
        if (_compatCreep) _compatFrac = Math.Min(0.9, _compatFrac + 0.006); // o passo leva tempo sem dizer quanto: anda devagar
        var target = Math.Min(1, (_compatDone + _compatFrac) / _compatSteps);
        _compatShown += (target - _compatShown) * 0.25;
        CompatBar.Width = ((FrameworkElement)CompatBar.Parent).ActualWidth * _compatShown;
        CompatPercent.Text = T($"{_compatShown * 100:0}%");
    }

    void CompatStep(string text, bool creep = true) { CompatStepText.Text = text; _compatCreep = creep; }
    void CompatStepDone() { _compatDone++; _compatFrac = 0; }

    /// <summary>Linha do log do script elevado → texto e avanço da barra (vem de outra thread).</summary>
    void CompatLine(string line) => Dispatcher.BeginInvoke(() =>
    {
        var msg = line.IndexOf(' ') is > 0 and var i ? line[(i + 1)..] : line;
        if (line.StartsWith("OK ")) CompatStepDone();
        else if (line.StartsWith("INFO ")) CompatStep(msg + "...");
    });

    async Task HideCompatProgress(bool ok)
    {
        if (ok)
        {
            (_compatDone, _compatFrac, _compatCreep) = (_compatSteps, 0, false);
            CompatStepText.Text = T("Concluído.");
            await Task.Delay(700);
        }
        _compatTimer.Stop();
        CompatOverlay.Visibility = Visibility.Collapsed;
    }

    void UpdateCompatUi()
    {
        var v = CompatMode.VJoy.IsInstalled();
        var h = CompatMode.HidHide.IsInstalled();
        CompatStatusText.Text = (v, h) switch
        {
            (true, true) => T("Drivers instalados (vJoy e HidHide). O app continua no Modo Padrão."),
            (false, false) => T("Desligado. Para jogos sem suporte ao G27: o jogo passa a ver um volante virtual (vJoy). Ativar instala dois drivers e pede para reiniciar."),
            _ => T($"Instalação incompleta: falta o {(v ? "HidHide" : T("vJoy"))}."),
        };
        CompatButton.Content = _compatBusy ? T("Aguarde...") : v && h ? T("Desinstalar os drivers") : v || h ? T("Completar a instalação") : T("Ativar (instala os drivers)");
        CompatButton.IsEnabled = !_compatBusy;
    }

    async void Compat_Click(object sender, RoutedEventArgs e)
    {
        AppSettingsPopup.IsOpen = false;
        bool uninstall = CompatMode.IsInstalled;
        var missing = CompatMode.All.Where(d => !d.IsInstalled()).ToArray();
        var ask = uninstall
            ? T("Desinstalar o vJoy e o HidHide?\n\nO G27 volta a aparecer para todos os jogos. O Windows vai pedir permissão de administrador e, no fim, é preciso reiniciar o PC.")
            : T("Ativar o Modo Compatibilidade Total?\n\n") +
              T("Neste modo o jogo verá \"vJoy Device\", não G27. Jogos com suporte ao G27 (Forza, BeamNG, ETS2...) funcionam melhor no Modo Padrão.\n\n") +
              T("O G27 Hub vai:\n") +
              T("1. criar um ponto de restauração do Windows;\n") +
              T($"2. baixar e instalar {string.Join(T(" e "), missing.Select(d => d.Name))} (instaladores oficiais, assinados e conferidos);\n") +
              T("3. pedir permissão de administrador;\n") +
              T("4. pedir para reiniciar o PC.\n\n") +
              T("Depois de instalar, o app continua no Modo Padrão até você ligar o modo. Para desfazer, use DESINSTALAR OS DRIVERS.");
        if (MessageBox.Show(this, ask, T("G27 Hub: Compatibilidade Total"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        _compatBusy = true;
        UpdateCompatUi();
        int? code = null;
        try
        {
            if (uninstall)
            {
                if (_vwheel != null) { StopCompatForExit(); _config.CompatOn = false; _config.Save(); UpdateCompatVisual(); }
                AppLog.Info("MODO", "Desinstalando os drivers da Compatibilidade Total");
                ShowCompatProgress(T("Desinstalando os drivers"), CompatMode.All.Length);
                CompatStep(T("Confirme a permissão de administrador do Windows."), creep: false);
                code = await Task.Run(() => CompatMode.RunElevated("desinstalar", CompatMode.UninstallScript, CompatLine));
            }
            else
            {
                AppLog.Info("MODO", "Ativando a Compatibilidade Total: baixando os instaladores oficiais");
                ShowCompatProgress(T("Instalando os drivers"), missing.Length * 2 + 1); // baixar cada um + ponto de restauração + instalar cada um
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                var files = new List<(DriverInstaller, string)>();
                foreach (var d in missing)
                {
                    CompatStep(T($"Baixando {d.Name} e conferindo a assinatura..."), creep: false);
                    files.Add((d, await CompatMode.Download(d, http, new Progress<double>(f => _compatFrac = f * 0.95))));
                    CompatStepDone();
                }
                CompatStep(T("Confirme a permissão de administrador do Windows."), creep: false);
                code = await Task.Run(() => CompatMode.RunElevated("instalar", log => CompatMode.InstallScript(files, log), CompatLine));
            }

            await HideCompatProgress(code == 0);
            if (code == null) return; // recusou o UAC (já logado)
            if (code != 0)
            {
                ShowToast("MODO", "Error", uninstall ? T("Falha ao desinstalar") : T("Falha ao instalar"), T("Veja os detalhes no console. Se precisar, o ponto de restauração desfaz tudo."), T("Ver detalhes"));
                return;
            }
            AppLog.Ok("MODO", uninstall ? "Drivers da Compatibilidade Total desinstalados" : "Drivers da Compatibilidade Total instalados");
            if (MessageBox.Show(this, (uninstall ? T("Drivers removidos.") : T("Drivers instalados.")) + T(" É preciso reiniciar o PC para terminar.\n\nReiniciar agora?"),
                    T("G27 Hub"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                AppLog.Info("MODO", "Reiniciando o PC a pedido do usuário");
                Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5 /c \"G27 Hub: reiniciando para terminar a Compatibilidade Total\"") { CreateNoWindow = true });
            }
        }
        catch (Exception ex)
        {
            await HideCompatProgress(false);
            AppLog.Erro("MODO", "Falha na Compatibilidade Total", ex);
            ShowToast("MODO", "Error", T("Falha na Compatibilidade Total"), ex.Message, T("Ver detalhes"));
        }
        finally
        {
            _compatBusy = false;
            UpdateCompatUi();
        }
    }
}
