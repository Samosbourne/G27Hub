using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using G27Hub.Core;
using G27Hub.Logging;
using static G27Hub.Core.Lang;

namespace G27Hub.App;

/// <summary>
/// Brincadeira da aba Volante (pedido do John): segurar o G27 desenhado com o mouse e girar; o volante de verdade
/// gira junto, empurrado pela força constante (WheelSpinner, numa thread de ~1 ms). Soltar o mouse solta o volante.
/// </summary>
public partial class MainWindow
{
    readonly WheelSpinner _spinner = new();
    volatile bool _spinning;
    double _spinTarget, _spinLastMouse;

    void StartSpin()
    {
        MainWheel.Cursor = Cursors.Hand;
        MainWheel.ToolTip = T("Segure e gire: o G27 de verdade gira junto");
        MainWheel.MouseLeftButtonDown += (_, e) => SpinBegin(e);
        MainWheel.MouseMove += (_, e) => { if (_spinning) SpinMove(e); };
        MainWheel.MouseLeftButtonUp += (_, _) => SpinEnd();
        MainWheel.LostMouseCapture += (_, _) => SpinEnd();
        Deactivated += (_, _) => SpinEnd();
    }

    /// <summary>Ângulo do mouse em volta do centro do desenho: 0° = em cima, positivo = sentido horário (direita).</summary>
    double MouseAngle(MouseEventArgs e)
    {
        var p = e.GetPosition(MainWheel);
        return Math.Atan2(p.X - MainWheel.ActualWidth / 2, MainWheel.ActualHeight / 2 - p.Y) * 180 / Math.PI;
    }

    // ================= TESTAR VOLANTE (como o LGS): Core/WheelTest =================

    volatile bool _wheelTesting;

    async void WheelTest_Click(object sender, RoutedEventArgs e)
    {
        if (_wheelTesting) { _wheelTesting = false; return; } // o mesmo botão para
        if (!_dev.IsConnected) { ShowToast("G27", "Warn", T("G27 não conectado"), T("Ligue o volante para testar.")); return; }
        if (FfbShared.Read() is { AgeMs: < 1000 })
        {
            ShowToast("FFB", "Warn", T("Tem um jogo usando o force feedback"), T("Feche o jogo antes de rodar o teste."));
            return;
        }
        SpinEnd();
        _wheelTesting = true;
        WheelTestButton.Content = T("Parar teste");
        AppLog.Info("FFB", "Teste do volante iniciado");
        var done = await Task.Run(() => WheelTest.Run(_dev, () => !_wheelTesting, s => Dispatcher.BeginInvoke(() => WheelTestStatus.Text = s + "…")));
        _wheelTesting = false;
        WheelTestButton.Content = T("Testar volante");
        WheelTestStatus.Text = done ? T("Pronto: o volante foi de ponta a ponta e voltou ao centro.") : T("Teste interrompido.");
        if (!done) AppLog.Info("FFB", "Teste do volante interrompido");
        ApplyAutocenter(); // devolve a mola
    }

    void SpinBegin(MouseButtonEventArgs e)
    {
        if (_wheelTesting || _spinning || !_dev.IsConnected || _dev.State?.Angle is not { } angle) return;
        if (FfbShared.Read() is { AgeMs: < 1000 })
        {
            ShowToast("FFB", "Warn", T("Tem um jogo usando o force feedback"), T("Feche o jogo para girar o volante pelo app."));
            return;
        }
        _spinTarget = angle;
        _spinLastMouse = MouseAngle(e);
        _spinner.Reset();
        try { _dev.SetAutocenter(0); } catch (Exception ex) { AppLog.Erro("FFB", "Girar pelo mouse", ex); return; }
        _spinning = true;
        MainWheel.CaptureMouse();
        AppLog.Info("FFB", "Girando o G27 pelo mouse");
        new Thread(SpinLoop) { IsBackground = true, Name = "Girar pelo mouse" }.Start();
    }

    void SpinMove(MouseEventArgs e)
    {
        var a = MouseAngle(e);
        var delta = a - _spinLastMouse;
        if (delta > 180) delta -= 360; // passou pelo "6 horas": continua girando no mesmo sentido
        if (delta < -180) delta += 360;
        _spinLastMouse = a;
        _spinTarget = Math.Clamp(_spinTarget + delta, -_dev.Range / 2.0, _dev.Range / 2.0);
    }

    void SpinEnd()
    {
        if (!_spinning) return;
        _spinning = false; // a thread para e solta a força
        if (MainWheel.IsMouseCaptured) MainWheel.ReleaseMouseCapture();
    }

    void SpinLoop()
    {
        var sw = Stopwatch.StartNew();
        double last = 0;
        int lastForce = int.MinValue;
        try
        {
            while (_spinning && _dev.IsConnected)
            {
                var t = sw.Elapsed.TotalSeconds;
                var f = _spinner.Step(_dev.State?.Angle ?? 0, _spinTarget, t - last);
                last = t;
                if (f != lastForce) { _dev.SetForce(f); lastForce = f; }
                Thread.Sleep(1);
            }
            _dev.SetForce(0);
        }
        catch (Exception ex) { AppLog.Erro("FFB", "Girar pelo mouse parou", ex); }
        _spinning = false;
        Dispatcher.BeginInvoke(() => { ApplyAutocenter(); AppLog.Info("FFB", "Volante solto"); });
    }
}
