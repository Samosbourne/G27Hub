using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using G27Hub.App.Controls;
using G27Hub.Core;
using G27Hub.Logging;
using static G27Hub.Core.Lang;

namespace G27Hub.App;

/// <summary>
/// Fase 7: LEDs de RPM. Uma thread lê a telemetria (TelemetryHub) e manda a máscara ao volante quando ela muda,
/// mesmo com o app na bandeja. Teste e atalho de rotação seguram os LEDs por um tempo (HoldLeds).
/// </summary>
public partial class MainWindow
{
    readonly TelemetryHub _telemetry = new();
    volatile int _ledMask; // o que está aceso no volante (-1 = reenviar)
    long _ledHoldUntil;
    volatile TelemetryFrame? _ledFrame;
    int _shownLedMask = -1;

    LedSettings L => _config.Leds;
    SettingSlider[] LedSliders => [LedT1, LedT2, LedT3, LedT4, LedT5];

    void StartLeds()
    {
        _telemetry.Start();
        _dev.ConnectionChanged += _ => _ledMask = -1; // volante religado começa apagado
        new Thread(() => { while (true) { LedTick(); Thread.Sleep(15); } }) { IsBackground = true, Name = "LEDs" }.Start();
    }

    void LedTick()
    {
        var now = Environment.TickCount64;
        var f = _ledFrame = _telemetry.Current;
        if (now < Interlocked.Read(ref _ledHoldUntil) || !_dev.IsConnected) return;
        int mask = f != null && L.On ? RpmLeds.Mask(f, L, now) : 0;
        if (mask == _ledMask) return;
        try { _dev.SetLeds(mask, log: false); _ledMask = mask; }
        catch (Exception ex) when (ex is IOException or InvalidOperationException) { } // desconectou no meio
    }

    /// <summary>A telemetria não mexe nos LEDs por ms milissegundos (teste, atalho de rotação).</summary>
    void HoldLeds(int ms)
    {
        Interlocked.Exchange(ref _ledHoldUntil, Environment.TickCount64 + ms);
        _ledMask = -1;
    }

    // ================= aba =================

    void BuildLedBar()
    {
        foreach (var c in new[] { "#35E06B", "#35E06B", "#FFC531", "#FF8A1F", "#FF3B3B" })
        {
            var b = new Border { Width = 64, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(6, 0, 6, 0), Background = Draw.Hex(c) };
            _ledBar.Add(b);
            LedBar.Children.Add(b);
        }
        SetLedVisual(0);
        LoadLedsIntoUi();
        for (int i = 0; i < 5; i++)
        {
            int n = i;
            LedSliders[i].ValueChanged += v => { L.Thresholds[n] = (int)v; _config.Save(); };
        }
    }

    void LoadLedsIntoUi()
    {
        LedOnCheck.IsChecked = L.On;
        LedModeProgressive.IsChecked = L.Mode == LedMode.Progressive;
        LedModeShift.IsChecked = L.Mode == LedMode.ShiftPoint;
        LedModeLimiter.IsChecked = L.Mode == LedMode.Limiter;
        for (int i = 0; i < 5; i++) LedSliders[i].Value = L.Thresholds[i];
        LedGameCheck.IsChecked = L.GameControls;
        LedPitCheck.IsChecked = L.PitLimiterBlink;
        LedFuelCheck.IsChecked = L.LowFuelBlink;
        LedModeHint.Text = L.Mode switch
        {
            LedMode.Progressive => T("A barra enche com a RPM; no LED 5 (ponto de troca) pisca inteira."),
            LedMode.ShiftPoint => T("Apagados até o ponto de troca (LED 5); aí piscam todos."),
            _ => T("A barra enche e fica acesa; pisca só no corte de giro."),
        };
        UpdateGameTelemetry();
    }

    void LedSetting_Click(object sender, RoutedEventArgs e)
    {
        L.On = LedOnCheck.IsChecked == true;
        L.Mode = LedModeShift.IsChecked == true ? LedMode.ShiftPoint : LedModeLimiter.IsChecked == true ? LedMode.Limiter : LedMode.Progressive;
        L.GameControls = LedGameCheck.IsChecked == true;
        L.PitLimiterBlink = LedPitCheck.IsChecked == true;
        L.LowFuelBlink = LedFuelCheck.IsChecked == true;
        _config.Save();
        LoadLedsIntoUi();
        AppLog.Info("LED", $"LEDs de RPM: {(L.On ? L.Mode.ToString() : "desligados")}");
    }

    void UpdateGameTelemetry()
    {
        var scs = ScsTelemetry.GameDirs();
        ScsTelemetryPanel.Visibility = scs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var scsOn = ScsTelemetry.IsInstalled(scs);
        ScsTelemetryText.Text = scsOn
            ? T("Euro Truck / American Truck Simulator: plugin de telemetria do G27 Hub instalado.")
            : T("Euro Truck / American Truck Simulator: precisa do plugin de telemetria do G27 Hub (o jogo pergunta uma vez se pode usar).");
        ScsTelemetryButton.Content = scsOn ? T("Remover do ETS2 / ATS") : T("Instalar no ETS2 / ATS");

        BeamNgTelemetryPanel.Visibility = BeamNgFix.IsInstalled() ? Visibility.Visible : Visibility.Collapsed;
        var on = BeamNgTelemetry.IsInstalled();
        BeamNgTelemetryText.Text = on
            ? T("BeamNG.drive: mod de telemetria do G27 Hub instalado (manda a RPM máxima de cada carro).")
            : T("BeamNG.drive: precisa de um mod pequeno do G27 Hub para mandar a RPM máxima de cada carro.");
        BeamNgTelemetryButton.Content = on ? T("Remover do BeamNG") : T("Instalar no BeamNG");
    }

    void BeamNgTelemetry_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (BeamNgTelemetry.IsInstalled()) BeamNgTelemetry.Uninstall();
            else
            {
                BeamNgTelemetry.Install();
                ShowToast("LED", "Ok", T("Telemetria do BeamNG instalada"), T("Vale na próxima vez que o BeamNG abrir. Para remover, use o mesmo botão."));
            }
        }
        catch (Exception ex)
        {
            AppLog.Erro("TELEMETRIA", "Não consegui mexer no mod do BeamNG", ex);
            ShowToast("LED", "Error", T("Falha no mod do BeamNG"), ex.Message, T("Ver detalhes"));
        }
        UpdateGameTelemetry();
    }

    void ScsTelemetry_Click(object sender, RoutedEventArgs e)
    {
        var dirs = ScsTelemetry.GameDirs();
        try
        {
            if (ScsTelemetry.IsInstalled(dirs)) ScsTelemetry.Uninstall(dirs);
            else
            {
                ScsTelemetry.Install(dirs);
                ShowToast("LED", "Ok", T("Telemetria do ETS2 / ATS instalada"), T("Ao abrir o jogo, ele avisa que achou um plugin: é só confirmar. Para remover, use o mesmo botão."));
            }
        }
        catch (Exception ex)
        {
            AppLog.Erro("TELEMETRIA", "Não consegui mexer no plugin do ETS2 / ATS (o jogo está aberto?)", ex);
            ShowToast("LED", "Error", T("Falha no plugin do ETS2 / ATS"), T("Feche o jogo e tente de novo."), T("Ver detalhes"));
        }
        UpdateGameTelemetry();
    }

    /// <summary>Chamado a 60 fps com a aba LEDs aberta.</summary>
    void UpdateLedsTab()
    {
        if (Environment.TickCount64 < Interlocked.Read(ref _ledHoldUntil)) return; // o teste desenha sozinho
        var f = _ledFrame;
        SetLedVisual(Math.Max(_ledMask, 0));
        LedText.Text = f == null ? T("sem telemetria: abra um jogo")
            : $"{f.Source} · {f.Rpm:N0}{(f.MaxRpm > 0 ? $" / {f.MaxRpm:N0}" : "")} rpm ({RpmLeds.Percent(f, L):0}%)" +
              (f.Gear is { } g ? $" · {(g < 0 ? "R" : g == 0 ? "N" : $"{g}ª")}" : "");
    }

    void SetLedVisual(int mask)
    {
        if (mask == _shownLedMask) return;
        _shownLedMask = mask;
        LedWheel.SetLeds(mask);
        for (int i = 0; i < _ledBar.Count; i++)
        {
            var on = (mask >> i & 1) != 0;
            ((SolidColorBrush)_ledBar[i].Background).Opacity = on ? 1 : 0.13;
            _ledBar[i].Effect = on ? new System.Windows.Media.Effects.DropShadowEffect { Color = ((SolidColorBrush)_ledBar[i].Background).Color, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.8 } : null;
        }
    }

    async void TestLeds_Click(object sender, RoutedEventArgs e) => await TestLeds();

    /// <summary>Simula uma acelerada: 60% → 100% da RPM em 2,5 s e segura 1,5 s no corte, com o modo e os limites atuais.</summary>
    async Task TestLeds()
    {
        if (!_dev.IsConnected) { ShowToast("LED", "Warn", T("G27 não conectado"), T("Ligue o volante para testar os LEDs.")); return; }
        AppLog.Info("LED", $"Teste dos LEDs: simulando uma acelerada (modo {L.Mode})");
        var start = Environment.TickCount64;
        for (long t = 0; t < 4000; t = Environment.TickCount64 - start)
        {
            HoldLeds(500);
            float pct = 60 + 40 * (float)Math.Min(t / 2500.0, 1);
            var mask = RpmLeds.Mask(new TelemetryFrame("teste", pct * 70, 7000, null), L, t);
            try { _dev.SetLeds(mask, log: false); } catch (Exception ex) when (ex is IOException or InvalidOperationException) { return; }
            SetLedVisual(mask);
            LedText.Text = T($"teste · {pct * 70:N0} / 7.000 rpm ({pct:0}%)");
            await Task.Delay(16);
        }
        HoldLeds(0);
        AppLog.Ok("LED", "Teste dos LEDs concluído");
    }
}
